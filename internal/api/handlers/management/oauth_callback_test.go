package management

import (
	"bytes"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/router-for-me/CLIProxyAPI/v6/internal/config"
	coreauth "github.com/router-for-me/CLIProxyAPI/v6/sdk/cliproxy/auth"
)

func TestPostOAuthCallbackAcceptsAlreadyProcessedState(t *testing.T) {
	gin.SetMode(gin.TestMode)

	previousStore := oauthSessions
	oauthSessions = newOAuthSessionStore(time.Minute)
	t.Cleanup(func() {
		oauthSessions = previousStore
	})

	RegisterOAuthSession("session-1", "codex")
	CompleteOAuthSession("session-1")
	CompleteOAuthSessionsByProvider("codex")

	h := &Handler{
		cfg: &config.Config{
			AuthDir: t.TempDir(),
		},
	}

	body := []byte(`{"provider":"codex","redirect_url":"http://localhost:1455/auth/callback?code=test-code&state=session-1"}`)
	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	req := httptest.NewRequest(http.MethodPost, "/oauth-callback", bytes.NewReader(body))
	req.Header.Set("Content-Type", "application/json")
	c.Request = req

	h.PostOAuthCallback(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want %d, body=%s", rec.Code, http.StatusOK, rec.Body.String())
	}
	if !bytes.Contains(rec.Body.Bytes(), []byte(`"already_processed":true`)) {
		t.Fatalf("expected already_processed response, got %s", rec.Body.String())
	}
}

func TestPostOAuthCallbackStoresImportContextForPendingSession(t *testing.T) {
	gin.SetMode(gin.TestMode)

	previousStore := oauthSessions
	oauthSessions = newOAuthSessionStore(time.Minute)
	t.Cleanup(func() {
		oauthSessions = previousStore
	})

	RegisterOAuthSession("session-import", "codex")

	h := &Handler{
		cfg: &config.Config{
			AuthDir: t.TempDir(),
		},
	}

	body := []byte(`{"provider":"codex","redirect_url":"http://localhost:1455/auth/callback?code=test-code&state=session-import","password":"secret-password","mailapi_url":"http://mail.test/latest"}`)
	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	req := httptest.NewRequest(http.MethodPost, "/oauth-callback", bytes.NewReader(body))
	req.Header.Set("Content-Type", "application/json")
	c.Request = req

	h.PostOAuthCallback(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want %d, body=%s", rec.Code, http.StatusOK, rec.Body.String())
	}

	password, mailAPIURL := GetOAuthSessionImportContext("session-import")
	if password != "secret-password" {
		t.Fatalf("password = %q, want secret-password", password)
	}
	if mailAPIURL != "http://mail.test/latest" {
		t.Fatalf("mailapi_url = %q, want http://mail.test/latest", mailAPIURL)
	}
}

func TestApplyOAuthSessionImportMetadataWritesPasswordAndMailbox(t *testing.T) {
	RegisterOAuthSession("session-apply", "codex")
	SetOAuthSessionImportContext("session-apply", "secret-password", "http://mail.test/latest")

	record := &coreauth.Auth{
		ID:       "codex-new.json",
		Provider: "codex",
		Metadata: map[string]any{
			"email": "recover@example.com",
		},
	}
	applyOAuthSessionImportMetadata("session-apply", record)

	if record.Metadata["password"] != "secret-password" {
		t.Fatalf("password metadata = %#v", record.Metadata["password"])
	}
	if record.Metadata["mailapi_url"] != "http://mail.test/latest" {
		t.Fatalf("mailapi_url metadata = %#v", record.Metadata["mailapi_url"])
	}
	if record.Mailbox == nil {
		t.Fatal("expected mailbox metadata to be set")
	}
}

func TestPostOAuthCallbackReturnsSessionStatusWhenFlowIsNoLongerPending(t *testing.T) {
	gin.SetMode(gin.TestMode)

	previousStore := oauthSessions
	oauthSessions = newOAuthSessionStore(time.Minute)
	t.Cleanup(func() {
		oauthSessions = previousStore
	})

	RegisterOAuthSession("session-timeout", "codex")
	SetOAuthSessionError("session-timeout", "Timeout waiting for OAuth callback")

	h := &Handler{
		cfg: &config.Config{
			AuthDir: t.TempDir(),
		},
	}

	body := []byte(`{"provider":"codex","redirect_url":"http://localhost:1455/auth/callback?code=test-code&state=session-timeout"}`)
	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	req := httptest.NewRequest(http.MethodPost, "/oauth-callback", bytes.NewReader(body))
	req.Header.Set("Content-Type", "application/json")
	c.Request = req

	h.PostOAuthCallback(c)

	if rec.Code != http.StatusConflict {
		t.Fatalf("status = %d, want %d, body=%s", rec.Code, http.StatusConflict, rec.Body.String())
	}
	if !bytes.Contains(rec.Body.Bytes(), []byte(`Timeout waiting for OAuth callback`)) {
		t.Fatalf("expected session status in response, got %s", rec.Body.String())
	}
}
