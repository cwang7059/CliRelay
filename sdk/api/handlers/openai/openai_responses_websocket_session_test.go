package openai

import (
	"context"
	"errors"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/gorilla/websocket"
	"github.com/router-for-me/CLIProxyAPI/v6/internal/registry"
	"github.com/router-for-me/CLIProxyAPI/v6/sdk/api/handlers"
	coreauth "github.com/router-for-me/CLIProxyAPI/v6/sdk/cliproxy/auth"
	coreexecutor "github.com/router-for-me/CLIProxyAPI/v6/sdk/cliproxy/executor"
	sdkconfig "github.com/router-for-me/CLIProxyAPI/v6/sdk/config"
	"github.com/tidwall/gjson"
)

type websocketStatusErr struct {
	status int
	msg    string
}

func (e websocketStatusErr) Error() string   { return e.msg }
func (e websocketStatusErr) StatusCode() int { return e.status }

type scriptedStreamOutcome struct {
	payloads [][]byte
	err      error
}

type scriptedResponsesWebsocketExecutor struct {
	mu        sync.Mutex
	outcomes  map[string][]scriptedStreamOutcome
	calls     []string
	closeHits []string
}

func (e *scriptedResponsesWebsocketExecutor) Identifier() string { return "codex" }

func (e *scriptedResponsesWebsocketExecutor) Execute(context.Context, *coreauth.Auth, coreexecutor.Request, coreexecutor.Options) (coreexecutor.Response, error) {
	return coreexecutor.Response{}, errors.New("not implemented")
}

func (e *scriptedResponsesWebsocketExecutor) ExecuteStream(_ context.Context, auth *coreauth.Auth, _ coreexecutor.Request, _ coreexecutor.Options) (*coreexecutor.StreamResult, error) {
	if auth == nil {
		return nil, errors.New("missing auth")
	}

	e.mu.Lock()
	e.calls = append(e.calls, auth.ID)
	queue := e.outcomes[auth.ID]
	if len(queue) == 0 {
		e.mu.Unlock()
		return nil, errors.New("missing scripted outcome")
	}
	outcome := queue[0]
	e.outcomes[auth.ID] = queue[1:]
	e.mu.Unlock()

	ch := make(chan coreexecutor.StreamChunk, len(outcome.payloads)+1)
	for _, payload := range outcome.payloads {
		ch <- coreexecutor.StreamChunk{Payload: payload}
	}
	if outcome.err != nil {
		ch <- coreexecutor.StreamChunk{Err: outcome.err}
	}
	close(ch)
	return &coreexecutor.StreamResult{Chunks: ch}, nil
}

func (e *scriptedResponsesWebsocketExecutor) Refresh(_ context.Context, auth *coreauth.Auth) (*coreauth.Auth, error) {
	return auth, nil
}

func (e *scriptedResponsesWebsocketExecutor) CountTokens(context.Context, *coreauth.Auth, coreexecutor.Request, coreexecutor.Options) (coreexecutor.Response, error) {
	return coreexecutor.Response{}, errors.New("not implemented")
}

func (e *scriptedResponsesWebsocketExecutor) HttpRequest(context.Context, *coreauth.Auth, *http.Request) (*http.Response, error) {
	return nil, errors.New("not implemented")
}

func (e *scriptedResponsesWebsocketExecutor) CloseExecutionSession(sessionID string) {
	e.mu.Lock()
	defer e.mu.Unlock()
	e.closeHits = append(e.closeHits, strings.TrimSpace(sessionID))
}

func (e *scriptedResponsesWebsocketExecutor) AuthIDs() []string {
	e.mu.Lock()
	defer e.mu.Unlock()
	return append([]string(nil), e.calls...)
}

func (e *scriptedResponsesWebsocketExecutor) ClosedSessions() []string {
	e.mu.Lock()
	defer e.mu.Unlock()
	return append([]string(nil), e.closeHits...)
}

func newResponsesWebsocketTestConn(t *testing.T, executor *scriptedResponsesWebsocketExecutor) (*websocket.Conn, func()) {
	t.Helper()
	gin.SetMode(gin.TestMode)

	manager := coreauth.NewManager(nil, &coreauth.FillFirstSelector{}, nil)
	manager.RegisterExecutor(executor)

	auth1 := &coreauth.Auth{
		ID:       "auth1",
		Provider: "codex",
		Status:   coreauth.StatusActive,
		Attributes: map[string]string{
			"priority":   "2",
			"websockets": "true",
		},
	}
	auth2 := &coreauth.Auth{
		ID:       "auth2",
		Provider: "codex",
		Status:   coreauth.StatusActive,
		Attributes: map[string]string{
			"priority":   "1",
			"websockets": "true",
		},
	}
	for _, auth := range []*coreauth.Auth{auth1, auth2} {
		if _, err := manager.Register(context.Background(), auth); err != nil {
			t.Fatalf("Register(%s): %v", auth.ID, err)
		}
		registry.GetGlobalRegistry().RegisterClient(auth.ID, auth.Provider, []*registry.ModelInfo{{ID: "test-model"}})
	}

	base := handlers.NewBaseAPIHandlers(&sdkconfig.SDKConfig{}, manager)
	h := NewOpenAIResponsesAPIHandler(base)

	engine := gin.New()
	engine.GET("/v1/responses", h.ResponsesWebsocket)
	server := httptest.NewServer(engine)

	wsURL := "ws" + strings.TrimPrefix(server.URL, "http") + "/v1/responses"
	conn, _, err := websocket.DefaultDialer.Dial(wsURL, nil)
	if err != nil {
		server.Close()
		t.Fatalf("dial websocket: %v", err)
	}

	cleanup := func() {
		_ = conn.Close()
		server.Close()
		registry.GetGlobalRegistry().UnregisterClient(auth1.ID)
		registry.GetGlobalRegistry().UnregisterClient(auth2.ID)
	}
	return conn, cleanup
}

func TestResponsesWebsocketRetriesWithNextAuthWhenPinnedAuthFailsBeforePayload(t *testing.T) {
	executor := &scriptedResponsesWebsocketExecutor{
		outcomes: map[string][]scriptedStreamOutcome{
			"auth1": {{
				err: websocketStatusErr{
					status: http.StatusTooManyRequests,
					msg:    `{"error":{"code":"insufficient_quota","message":"quota reached"}}`,
				},
			}},
			"auth2": {{
				payloads: [][]byte{
					[]byte(`{"type":"response.completed","response":{"id":"resp-2","output":[]}}`),
				},
			}},
		},
	}
	conn, cleanup := newResponsesWebsocketTestConn(t, executor)
	defer cleanup()

	if err := conn.WriteMessage(websocket.TextMessage, []byte(`{"type":"response.create","model":"test-model","input":[]}`)); err != nil {
		t.Fatalf("write message: %v", err)
	}

	if err := conn.SetReadDeadline(time.Now().Add(2 * time.Second)); err != nil {
		t.Fatalf("set read deadline: %v", err)
	}
	_, payload, err := conn.ReadMessage()
	if err != nil {
		t.Fatalf("read message: %v", err)
	}
	if got := gjson.GetBytes(payload, "type").String(); got != "response.done" {
		t.Fatalf("payload type = %q, want response.done; payload=%s", got, string(payload))
	}

	if got := executor.AuthIDs(); len(got) != 2 || got[0] != "auth1" || got[1] != "auth2" {
		t.Fatalf("auth sequence = %v, want [auth1 auth2]", got)
	}
	if closed := executor.ClosedSessions(); len(closed) == 0 || strings.TrimSpace(closed[0]) == "" {
		t.Fatalf("expected pinned session release, got %v", closed)
	}
}

func TestResponsesWebsocketClearsPinnedAuthForNextTurnAfterStreamedQuotaError(t *testing.T) {
	executor := &scriptedResponsesWebsocketExecutor{
		outcomes: map[string][]scriptedStreamOutcome{
			"auth1": {{
				payloads: [][]byte{
					[]byte(`{"type":"response.output_text.delta","delta":"hello"}`),
				},
				err: websocketStatusErr{
					status: http.StatusTooManyRequests,
					msg:    `{"error":{"code":"insufficient_quota","message":"quota reached"}}`,
				},
			}},
			"auth2": {{
				payloads: [][]byte{
					[]byte(`{"type":"response.completed","response":{"id":"resp-3","output":[]}}`),
				},
			}},
		},
	}
	conn, cleanup := newResponsesWebsocketTestConn(t, executor)
	defer cleanup()

	if err := conn.WriteMessage(websocket.TextMessage, []byte(`{"type":"response.create","model":"test-model","input":[]}`)); err != nil {
		t.Fatalf("write first message: %v", err)
	}

	if err := conn.SetReadDeadline(time.Now().Add(2 * time.Second)); err != nil {
		t.Fatalf("set first read deadline: %v", err)
	}
	_, payload, err := conn.ReadMessage()
	if err != nil {
		t.Fatalf("read first payload: %v", err)
	}
	if got := gjson.GetBytes(payload, "type").String(); got != "response.output_text.delta" {
		t.Fatalf("first payload type = %q, want response.output_text.delta; payload=%s", got, string(payload))
	}

	_, payload, err = conn.ReadMessage()
	if err != nil {
		t.Fatalf("read error payload: %v", err)
	}
	if got := gjson.GetBytes(payload, "type").String(); got != "error" {
		t.Fatalf("second payload type = %q, want error; payload=%s", got, string(payload))
	}

	if err := conn.WriteMessage(websocket.TextMessage, []byte(`{"type":"response.create","model":"test-model","input":[]}`)); err != nil {
		t.Fatalf("write second message: %v", err)
	}

	if err := conn.SetReadDeadline(time.Now().Add(2 * time.Second)); err != nil {
		t.Fatalf("set second read deadline: %v", err)
	}
	_, payload, err = conn.ReadMessage()
	if err != nil {
		t.Fatalf("read second turn payload: %v", err)
	}
	if got := gjson.GetBytes(payload, "type").String(); got != "response.done" {
		t.Fatalf("second turn payload type = %q, want response.done; payload=%s", got, string(payload))
	}

	if got := executor.AuthIDs(); len(got) != 2 || got[0] != "auth1" || got[1] != "auth2" {
		t.Fatalf("auth sequence = %v, want [auth1 auth2]", got)
	}
	if closed := executor.ClosedSessions(); len(closed) == 0 || strings.TrimSpace(closed[0]) == "" {
		t.Fatalf("expected pinned session release after streamed error, got %v", closed)
	}
}
