package management

import (
	"bytes"
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/router-for-me/CLIProxyAPI/v6/internal/auth/claude"
	"github.com/router-for-me/CLIProxyAPI/v6/internal/config"
	coreauth "github.com/router-for-me/CLIProxyAPI/v6/sdk/cliproxy/auth"
)

type failingAuthStore struct {
	items map[string]*coreauth.Auth
}

func (s *failingAuthStore) List(ctx context.Context) ([]*coreauth.Auth, error) {
	_ = ctx
	out := make([]*coreauth.Auth, 0, len(s.items))
	for _, item := range s.items {
		out = append(out, item.Clone())
	}
	return out, nil
}

func (s *failingAuthStore) Save(ctx context.Context, auth *coreauth.Auth) (string, error) {
	_ = ctx
	_ = auth
	return "", errors.New("persist failed")
}

func (s *failingAuthStore) Delete(ctx context.Context, id string) error {
	_ = ctx
	_ = id
	return nil
}

func TestIsCodex401RecoverableAllowsDisabledCodexOAuth(t *testing.T) {
	auth := &coreauth.Auth{
		ID:       "codex-disabled.json",
		FileName: "codex-disabled.json",
		Provider: "codex",
		Disabled: true,
		Status:   coreauth.StatusDisabled,
		Metadata: map[string]any{
			"email": "recover@example.com",
		},
	}

	if !isCodex401Recoverable(auth) {
		t.Fatal("expected disabled Codex OAuth auth to be recoverable")
	}

	auth.Provider = "claude"
	if isCodex401Recoverable(auth) {
		t.Fatal("expected non-Codex auth to be non-recoverable")
	}
}

func TestRecoveryContextFromAuthIncludesLoginIdentityAndPassword(t *testing.T) {
	auth := &coreauth.Auth{
		ID:       "codex-disabled.json",
		FileName: "codex-disabled.json",
		Provider: "codex",
		Disabled: true,
		Status:   coreauth.StatusDisabled,
		Metadata: map[string]any{
			"login_identity": "recover@example.com",
			"password":       "secret-password",
		},
	}

	recovery := recoveryContextFromAuth(auth)
	if recovery == nil {
		t.Fatal("expected recovery context")
	}
	if recovery.TargetEmail != "recover@example.com" {
		t.Fatalf("TargetEmail = %q", recovery.TargetEmail)
	}
	if recovery.TargetPassword != "secret-password" {
		t.Fatalf("TargetPassword = %q", recovery.TargetPassword)
	}
}

func TestGetCodex401RecoveryCredentialsReturnsSessionAccount(t *testing.T) {
	gin.SetMode(gin.TestMode)
	oldSessions := oauthSessions
	oauthSessions = newOAuthSessionStore(oauthSessionTTL)
	t.Cleanup(func() { oauthSessions = oldSessions })

	RegisterOAuthSessionRecovery("state-credentials", "codex", &oauthRecoveryContext{
		TargetID:       "codex-old.json",
		TargetName:     "codex-old.json",
		TargetEmail:    "recover@example.com",
		TargetPassword: "secret-password",
	})
	h := NewHandler(&config.Config{}, "", nil)
	t.Cleanup(h.Close)

	req := httptest.NewRequest(http.MethodGet, "/credentials?state=state-credentials", nil)
	w := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(w)
	c.Request = req

	h.GetCodex401RecoveryCredentials(c)

	if w.Code != http.StatusOK {
		t.Fatalf("status = %d, body = %s", w.Code, w.Body.String())
	}
	var payload struct {
		Email       string `json:"email"`
		Password    string `json:"password"`
		HasPassword bool   `json:"has_password"`
	}
	if err := json.Unmarshal(w.Body.Bytes(), &payload); err != nil {
		t.Fatalf("decode response: %v", err)
	}
	if payload.Email != "recover@example.com" || payload.Password != "secret-password" || !payload.HasPassword {
		t.Fatalf("unexpected payload: %#v", payload)
	}
}

func TestAutoRecoverCodex401ConfigEndpoint(t *testing.T) {
	gin.SetMode(gin.TestMode)
	configPath := filepath.Join(t.TempDir(), "config.yaml")
	if err := os.WriteFile(configPath, []byte("auto-recover-codex-401: true\n"), 0o600); err != nil {
		t.Fatalf("write config: %v", err)
	}
	h := NewHandler(&config.Config{AutoRecoverCodex401: true}, configPath, nil)
	t.Cleanup(h.Close)

	body, err := json.Marshal(map[string]any{"value": false})
	if err != nil {
		t.Fatalf("marshal body: %v", err)
	}
	req := httptest.NewRequest(http.MethodPut, "/auto-recover-codex-401", bytes.NewReader(body))
	req.Header.Set("Content-Type", "application/json")
	w := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(w)
	c.Request = req

	h.PutAutoRecoverCodex401(c)

	if w.Code != http.StatusOK {
		t.Fatalf("status = %d, body = %s", w.Code, w.Body.String())
	}
	if h.cfg.AutoRecoverCodex401 {
		t.Fatal("expected auto recover setting to be disabled")
	}
}

func TestSortAuthFileEntriesPutsAvailableFirst(t *testing.T) {
	files := []gin.H{
		{
			"name":        "disabled.json",
			"provider":    "codex",
			"status":      "disabled",
			"disabled":    true,
			"recoverable": true,
		},
		{
			"name":         "limited.json",
			"provider":     "codex",
			"status":       "error",
			"unavailable":  true,
			"restrictions": []gin.H{{"scope": "auth"}},
		},
		{
			"name":     "available-b.json",
			"provider": "codex",
			"status":   "active",
		},
		{
			"name":     "available-a.json",
			"provider": "codex",
			"status":   "active",
		},
	}

	sortAuthFileEntries(files)

	got := []string{
		files[0]["name"].(string),
		files[1]["name"].(string),
		files[2]["name"].(string),
		files[3]["name"].(string),
	}
	want := []string{"available-a.json", "available-b.json", "limited.json", "disabled.json"}
	for i := range want {
		if got[i] != want[i] {
			t.Fatalf("sorted[%d] = %q, want %q (all=%v)", i, got[i], want[i], got)
		}
	}
}

func TestSaveRecoveredCodexAuthOverwritesTargetAndReactivates(t *testing.T) {
	store := &memoryAuthStore{}
	manager := coreauth.NewManager(store, nil, nil)
	ctx := context.Background()
	_, err := manager.Register(ctx, &coreauth.Auth{
		ID:       "codex-old.json",
		FileName: "codex-old.json",
		Provider: "codex",
		Label:    "Old Label",
		Disabled: true,
		Status:   coreauth.StatusDisabled,
		Metadata: map[string]any{
			"email":           "recover@example.com",
			"account_id":      "account-1",
			"disabled":        true,
			"disabled_reason": "401_unauthorized",
			"label":           "Old Label",
			"custom_tags":     []string{"keep"},
		},
		LastError: &coreauth.Error{HTTPStatus: http.StatusUnauthorized, Message: "unauthorized"},
	})
	if err != nil {
		t.Fatalf("register target auth: %v", err)
	}

	h := &Handler{
		cfg:         &config.Config{},
		authManager: manager,
	}
	path, err := h.saveRecoveredCodexAuth(ctx, &oauthRecoveryContext{
		TargetID:        "codex-old.json",
		TargetName:      "codex-old.json",
		TargetFileName:  "codex-old.json",
		TargetEmail:     "recover@example.com",
		TargetAccountID: "account-1",
	}, &coreauth.Auth{
		ID:       "codex-new.json",
		FileName: "codex-new.json",
		Provider: "codex",
		Metadata: map[string]any{
			"email":      "recover@example.com",
			"account_id": "account-1",
			"plan_type":  "plus",
		},
	})
	if err != nil {
		t.Fatalf("save recovered auth: %v", err)
	}
	if path == "" {
		t.Fatal("expected saved path or id")
	}

	updated, ok := manager.GetByID("codex-old.json")
	if !ok || updated == nil {
		t.Fatal("expected target auth to remain under original id")
	}
	if updated.Disabled || updated.Status != coreauth.StatusActive {
		t.Fatalf("expected recovered auth active, got disabled=%v status=%q", updated.Disabled, updated.Status)
	}
	if updated.FileName != "codex-old.json" {
		t.Fatalf("filename = %q, want original filename", updated.FileName)
	}
	if updated.LastError != nil {
		t.Fatalf("expected LastError cleared, got %#v", updated.LastError)
	}
	if disabled, _ := updated.Metadata["disabled"].(bool); disabled {
		t.Fatal("metadata disabled should be false")
	}
	if _, ok := updated.Metadata["disabled_reason"]; ok {
		t.Fatal("disabled_reason should be removed")
	}
	if updated.Metadata["label"] != "Old Label" {
		t.Fatalf("label metadata = %#v, want Old Label", updated.Metadata["label"])
	}
	if updated.Metadata["plan_type"] != "plus" {
		t.Fatalf("plan_type = %#v, want plus", updated.Metadata["plan_type"])
	}
}

func TestSaveRecoveredCodexAuthRejectsEmailMismatch(t *testing.T) {
	manager := coreauth.NewManager(&memoryAuthStore{}, nil, nil)
	h := &Handler{
		cfg:         &config.Config{},
		authManager: manager,
	}

	_, err := h.saveRecoveredCodexAuth(context.Background(), &oauthRecoveryContext{
		TargetID:    "codex-old.json",
		TargetEmail: "expected@example.com",
	}, &coreauth.Auth{
		ID:       "codex-new.json",
		FileName: "codex-new.json",
		Provider: "codex",
		Metadata: map[string]any{
			"email": "other@example.com",
		},
	})
	if err == nil {
		t.Fatal("expected email mismatch error")
	}
}

func TestBeginAutoCodex401RecoveryDeduplicatesAndExpires(t *testing.T) {
	h := NewHandler(&config.Config{AutoRecoverCodex401: true}, "", nil)
	t.Cleanup(h.Close)

	if !h.beginAutoCodex401Recovery("codex-old.json") {
		t.Fatal("expected first recovery attempt to start")
	}
	if h.beginAutoCodex401Recovery("codex-old.json") {
		t.Fatal("expected duplicate recovery attempt to be suppressed")
	}

	h.autoRecoveryMu.Lock()
	h.autoRecoveryInFlight["codex-old.json"] = time.Now().Add(-autoCodex401RecoveryCooldown - time.Second)
	h.autoRecoveryInFlight[autoCodex401RecoveryGlobalKey] = time.Now().Add(-autoCodex401RecoveryCooldown - time.Second)
	h.autoRecoveryMu.Unlock()

	if !h.beginAutoCodex401Recovery("codex-old.json") {
		t.Fatal("expected expired recovery attempt to start again")
	}
}

func TestFinishAutoCodex401RecoveryClearsInFlight(t *testing.T) {
	h := NewHandler(&config.Config{AutoRecoverCodex401: true}, "", nil)
	t.Cleanup(h.Close)

	if !h.beginAutoCodex401Recovery("codex-old.json") {
		t.Fatal("expected first recovery attempt to start")
	}
	h.finishAutoCodex401Recovery("codex-old.json")
	if !h.beginAutoCodex401Recovery("codex-old.json") {
		t.Fatal("expected recovery attempt after finish to start")
	}
}

func TestBeginAutoCodex401RecoverySuppressesConcurrentDifferentAuth(t *testing.T) {
	h := NewHandler(&config.Config{AutoRecoverCodex401: true}, "", nil)
	t.Cleanup(h.Close)

	if !h.beginAutoCodex401Recovery("codex-a.json") {
		t.Fatal("expected first recovery attempt to start")
	}
	if h.beginAutoCodex401Recovery("codex-b.json") {
		t.Fatal("expected concurrent recovery for different auth to be suppressed")
	}
}

func TestPatchAuthFileFieldsUpdatesOAuthChannelLabel(t *testing.T) {
	gin.SetMode(gin.TestMode)

	store := &memoryAuthStore{}
	manager := coreauth.NewManager(store, nil, nil)
	_, err := manager.Register(context.Background(), &coreauth.Auth{
		ID:       "oauth-auth-1",
		FileName: "oauth-auth-1.json",
		Provider: "claude",
		Metadata: map[string]any{
			"email": "old@example.com",
		},
	})
	if err != nil {
		t.Fatalf("register auth: %v", err)
	}

	h := &Handler{
		cfg:         &config.Config{},
		authManager: manager,
	}

	body, err := json.Marshal(map[string]any{
		"name":  "oauth-auth-1.json",
		"label": "Team Alpha",
	})
	if err != nil {
		t.Fatalf("marshal body: %v", err)
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPatch, "/auth-files/fields", bytes.NewReader(body))
	c.Request.Header.Set("Content-Type", "application/json")

	h.PatchAuthFileFields(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected status %d, got %d, body=%s", http.StatusOK, rec.Code, rec.Body.String())
	}

	updated, ok := manager.GetByID("oauth-auth-1")
	if !ok || updated == nil {
		t.Fatal("expected updated auth")
	}
	if updated.Label != "Team Alpha" {
		t.Fatalf("label = %q, want %q", updated.Label, "Team Alpha")
	}
	if got, _ := updated.Metadata["label"].(string); got != "Team Alpha" {
		t.Fatalf("metadata label = %q, want %q", got, "Team Alpha")
	}
}

func TestPatchAuthFileFieldsUpdatesCustomTagsAndHiddenDefaultTags(t *testing.T) {
	gin.SetMode(gin.TestMode)

	store := &memoryAuthStore{}
	manager := coreauth.NewManager(store, nil, nil)
	_, err := manager.Register(context.Background(), &coreauth.Auth{
		ID:       "oauth-auth-tags",
		FileName: "oauth-auth-tags.json",
		Provider: "codex",
		Metadata: map[string]any{
			"email":     "tags@example.com",
			"plan_type": "pro",
		},
	})
	if err != nil {
		t.Fatalf("register auth: %v", err)
	}

	h := &Handler{
		cfg:         &config.Config{},
		authManager: manager,
	}

	body, err := json.Marshal(map[string]any{
		"name":                "oauth-auth-tags.json",
		"custom_tags":         []string{" Team ", "priority", "team"},
		"hidden_default_tags": []string{"pro", " codex "},
	})
	if err != nil {
		t.Fatalf("marshal body: %v", err)
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPatch, "/auth-files/fields", bytes.NewReader(body))
	c.Request.Header.Set("Content-Type", "application/json")

	h.PatchAuthFileFields(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected status %d, got %d, body=%s", http.StatusOK, rec.Code, rec.Body.String())
	}

	updated, ok := manager.GetByID("oauth-auth-tags")
	if !ok || updated == nil {
		t.Fatal("expected updated auth")
	}
	customTags, ok := updated.Metadata["custom_tags"].([]string)
	if !ok {
		t.Fatalf("custom_tags type = %T, want []string", updated.Metadata["custom_tags"])
	}
	if len(customTags) != 2 || customTags[0] != "team" || customTags[1] != "priority" {
		t.Fatalf("custom_tags = %#v, want [team priority]", customTags)
	}
	hiddenTags, ok := updated.Metadata["hidden_default_tags"].([]string)
	if !ok {
		t.Fatalf("hidden_default_tags type = %T, want []string", updated.Metadata["hidden_default_tags"])
	}
	if len(hiddenTags) != 2 || hiddenTags[0] != "pro" || hiddenTags[1] != "codex" {
		t.Fatalf("hidden_default_tags = %#v, want [pro codex]", hiddenTags)
	}
}

func TestPatchAuthFileFieldsUpdatesDisplayTags(t *testing.T) {
	gin.SetMode(gin.TestMode)

	store := &memoryAuthStore{}
	manager := coreauth.NewManager(store, nil, nil)
	_, err := manager.Register(context.Background(), &coreauth.Auth{
		ID:       "oauth-auth-display-tags",
		FileName: "oauth-auth-display-tags.json",
		Provider: "codex",
		Metadata: map[string]any{
			"email":     "display-tags@example.com",
			"plan_type": "pro",
		},
	})
	if err != nil {
		t.Fatalf("register auth: %v", err)
	}

	h := &Handler{
		cfg:         &config.Config{},
		authManager: manager,
	}

	body, err := json.Marshal(map[string]any{
		"name":         "oauth-auth-display-tags.json",
		"custom_tags":  []string{"vip"},
		"display_tags": []string{"codex", "vip"},
	})
	if err != nil {
		t.Fatalf("marshal body: %v", err)
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPatch, "/auth-files/fields", bytes.NewReader(body))
	c.Request.Header.Set("Content-Type", "application/json")

	h.PatchAuthFileFields(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected status %d, got %d, body=%s", http.StatusOK, rec.Code, rec.Body.String())
	}

	updated, ok := manager.GetByID("oauth-auth-display-tags")
	if !ok || updated == nil {
		t.Fatal("expected updated auth")
	}
	displayTags, ok := updated.Metadata["display_tags"].([]string)
	if !ok {
		t.Fatalf("display_tags type = %T, want []string", updated.Metadata["display_tags"])
	}
	if len(displayTags) != 2 || displayTags[0] != "codex" || displayTags[1] != "vip" {
		t.Fatalf("display_tags = %#v, want [codex vip]", displayTags)
	}
}

func TestPatchAuthFileFieldsRejectsMoreThanThreeCustomTags(t *testing.T) {
	gin.SetMode(gin.TestMode)

	store := &memoryAuthStore{}
	manager := coreauth.NewManager(store, nil, nil)
	_, err := manager.Register(context.Background(), &coreauth.Auth{
		ID:       "oauth-auth-too-many-tags",
		FileName: "oauth-auth-too-many-tags.json",
		Provider: "codex",
		Metadata: map[string]any{
			"email": "tags@example.com",
		},
	})
	if err != nil {
		t.Fatalf("register auth: %v", err)
	}

	h := &Handler{
		cfg:         &config.Config{},
		authManager: manager,
	}

	body, err := json.Marshal(map[string]any{
		"name":        "oauth-auth-too-many-tags.json",
		"custom_tags": []string{"one", "two", "three", "four"},
	})
	if err != nil {
		t.Fatalf("marshal body: %v", err)
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPatch, "/auth-files/fields", bytes.NewReader(body))
	c.Request.Header.Set("Content-Type", "application/json")

	h.PatchAuthFileFields(c)

	if rec.Code != http.StatusBadRequest {
		t.Fatalf("expected status %d, got %d, body=%s", http.StatusBadRequest, rec.Code, rec.Body.String())
	}
}

func TestBuildAuthFileEntryIncludesDefaultAndDisplayTags(t *testing.T) {
	auth := &coreauth.Auth{
		ID:       "codex-tagged-auth",
		FileName: "codex-tagged-auth.json",
		Provider: "codex",
		Attributes: map[string]string{
			"path": "codex-tagged-auth.json",
		},
		Metadata: map[string]any{
			"email":               "tagged@example.com",
			"plan_type":           "pro",
			"custom_tags":         []string{"team-a"},
			"hidden_default_tags": []string{"pro"},
		},
	}

	entry := (&Handler{}).buildAuthFileEntry(auth)
	if entry == nil {
		t.Fatal("expected auth file entry")
	}
	defaultTags, ok := entry["default_tags"].([]string)
	if !ok {
		t.Fatalf("default_tags type = %T, want []string", entry["default_tags"])
	}
	if len(defaultTags) != 2 || defaultTags[0] != "codex" || defaultTags[1] != "pro" {
		t.Fatalf("default_tags = %#v, want [codex pro]", defaultTags)
	}
	displayTags, ok := entry["display_tags"].([]string)
	if !ok {
		t.Fatalf("display_tags type = %T, want []string", entry["display_tags"])
	}
	if len(displayTags) != 2 || displayTags[0] != "codex" || displayTags[1] != "team-a" {
		t.Fatalf("display_tags = %#v, want [codex team-a]", displayTags)
	}
}

func TestBuildAuthFileEntryHonorsExplicitEmptyDisplayTags(t *testing.T) {
	auth := &coreauth.Auth{
		ID:       "codex-hidden-tags",
		FileName: "codex-hidden-tags.json",
		Provider: "codex",
		Attributes: map[string]string{
			"path": "codex-hidden-tags.json",
		},
		Metadata: map[string]any{
			"plan_type":    "pro",
			"custom_tags":  []string{"vip"},
			"display_tags": []string{},
		},
	}

	entry := (&Handler{}).buildAuthFileEntry(auth)
	if entry == nil {
		t.Fatal("expected auth file entry")
	}
	displayTags, ok := entry["display_tags"].([]string)
	if !ok {
		t.Fatalf("display_tags type = %T, want []string", entry["display_tags"])
	}
	if len(displayTags) != 0 {
		t.Fatalf("display_tags = %#v, want empty list", displayTags)
	}
}

func TestBuildAuthFileEntryReplacesStaleExplicitPlanDisplayTag(t *testing.T) {
	auth := &coreauth.Auth{
		ID:       "codex-downgraded-tags",
		FileName: "codex-downgraded-tags.json",
		Provider: "codex",
		Attributes: map[string]string{
			"path": "codex-downgraded-tags.json",
		},
		Metadata: map[string]any{
			"plan_type":    "free",
			"display_tags": []string{"codex", "plus"},
		},
	}

	entry := (&Handler{}).buildAuthFileEntry(auth)
	if entry == nil {
		t.Fatal("expected auth file entry")
	}
	defaultTags, ok := entry["default_tags"].([]string)
	if !ok {
		t.Fatalf("default_tags type = %T, want []string", entry["default_tags"])
	}
	if len(defaultTags) != 2 || defaultTags[0] != "codex" || defaultTags[1] != "free" {
		t.Fatalf("default_tags = %#v, want [codex free]", defaultTags)
	}
	displayTags, ok := entry["display_tags"].([]string)
	if !ok {
		t.Fatalf("display_tags type = %T, want []string", entry["display_tags"])
	}
	if len(displayTags) != 2 || displayTags[0] != "codex" || displayTags[1] != "free" {
		t.Fatalf("display_tags = %#v, want [codex free]", displayTags)
	}
}

func TestBuildAuthFileEntryExposesMetadataPlanTypeBeforeIDTokenClaim(t *testing.T) {
	idToken := makeManagementJWTForTest(t, map[string]any{
		"https://api.openai.com/auth": map[string]any{
			"chatgpt_plan_type":  "plus",
			"chatgpt_account_id": "acct_123",
		},
	})
	auth := &coreauth.Auth{
		ID:       "codex-stale-id-token",
		FileName: "codex-stale-id-token.json",
		Provider: "codex",
		Attributes: map[string]string{
			"path": "codex-stale-id-token.json",
		},
		Metadata: map[string]any{
			"plan_type": "free",
			"id_token":  idToken,
		},
	}

	entry := (&Handler{}).buildAuthFileEntry(auth)
	if entry == nil {
		t.Fatal("expected auth file entry")
	}
	if got, _ := entry["plan_type"].(string); got != "free" {
		t.Fatalf("plan_type = %q, want free", got)
	}
	claims, ok := entry["id_token"].(gin.H)
	if !ok {
		t.Fatalf("id_token type = %T, want gin.H", entry["id_token"])
	}
	if got, _ := claims["plan_type"].(string); got != "plus" {
		t.Fatalf("id_token.plan_type = %q, want plus", got)
	}
}

func TestBuildAuthFileEntryBackfillsCodexAccountIDFromDefaultOrganization(t *testing.T) {
	idToken := makeManagementJWTForTest(t, map[string]any{
		"https://api.openai.com/auth": map[string]any{
			"chatgpt_plan_type": "free",
			"organizations": []any{
				map[string]any{
					"id":         "org-default",
					"is_default": true,
					"role":       "owner",
					"title":      "Personal",
				},
			},
			"user_id": "user-abc123",
		},
	})
	auth := &coreauth.Auth{
		ID:       "codex-missing-account",
		FileName: "codex-missing-account.json",
		Provider: "codex",
		Attributes: map[string]string{
			"path": "codex-missing-account.json",
		},
		Metadata: map[string]any{
			"id_token": idToken,
		},
	}

	entry := (&Handler{}).buildAuthFileEntry(auth)
	if entry == nil {
		t.Fatal("expected auth file entry")
	}
	if got, _ := entry["account_id"].(string); got != "org-default" {
		t.Fatalf("account_id = %q, want org-default", got)
	}
	claims, ok := entry["id_token"].(gin.H)
	if !ok {
		t.Fatalf("id_token type = %T, want gin.H", entry["id_token"])
	}
	if got, _ := claims["chatgpt_account_id"].(string); got != "org-default" {
		t.Fatalf("id_token.chatgpt_account_id = %q, want org-default", got)
	}
}

func makeManagementJWTForTest(t *testing.T, claims map[string]any) string {
	t.Helper()
	encode := func(v any) string {
		raw, err := json.Marshal(v)
		if err != nil {
			t.Fatalf("marshal jwt part: %v", err)
		}
		return base64.RawURLEncoding.EncodeToString(raw)
	}
	return encode(map[string]any{"alg": "none", "typ": "JWT"}) + "." + encode(claims) + ".sig"
}

func TestBuildAuthFileEntryIncludesActiveRestrictions(t *testing.T) {
	nextRetry := time.Now().Add(34*time.Minute + 50*time.Second).UTC().Truncate(time.Second)
	auth := &coreauth.Auth{
		ID:       "codex-restricted",
		FileName: "codex-restricted.json",
		Provider: "codex",
		Status:   coreauth.StatusError,
		Attributes: map[string]string{
			"path": "codex-restricted.json",
		},
		ModelStates: map[string]*coreauth.ModelState{
			"gpt-5": {
				Status:         coreauth.StatusError,
				StatusMessage:  "unauthorized",
				Unavailable:    true,
				NextRetryAfter: nextRetry,
				LastError:      &coreauth.Error{Message: "unauthorized", HTTPStatus: http.StatusUnauthorized},
			},
		},
	}

	entry := (&Handler{}).buildAuthFileEntry(auth)
	if entry == nil {
		t.Fatal("expected auth file entry")
	}
	restrictions, ok := entry["restrictions"].([]gin.H)
	if !ok {
		t.Fatalf("restrictions type = %T, want []gin.H", entry["restrictions"])
	}
	if len(restrictions) != 1 {
		t.Fatalf("restrictions length = %d, want 1", len(restrictions))
	}
	got := restrictions[0]
	if got["scope"] != "model" || got["model"] != "gpt-5" || got["http_status"] != http.StatusUnauthorized {
		t.Fatalf("restriction = %#v, want model gpt-5 401", got)
	}
	if got["status_message"] != "unauthorized" {
		t.Fatalf("status_message = %#v, want unauthorized", got["status_message"])
	}
	if retry, ok := got["next_retry_after"].(time.Time); !ok || !retry.Equal(nextRetry) {
		t.Fatalf("next_retry_after = %#v, want %v", got["next_retry_after"], nextRetry)
	}
}

func TestBuildAuthFileEntryDedupesDuplicateRestrictions(t *testing.T) {
	now := time.Now().UTC().Truncate(time.Second)
	nextRetry := now.Add(25 * time.Minute)
	nextRecover := now.Add(25 * time.Minute)

	makeModelState := func() *coreauth.ModelState {
		return &coreauth.ModelState{
			Status:         coreauth.StatusError,
			StatusMessage:  `{"error":{"type":"usage_limit_reached"}}`,
			Unavailable:    true,
			NextRetryAfter: nextRetry,
			LastError:      &coreauth.Error{Message: "usage limit reached", HTTPStatus: http.StatusTooManyRequests},
			Quota: coreauth.QuotaState{
				Exceeded:      true,
				Reason:        "quota",
				NextRecoverAt: nextRecover,
			},
		}
	}

	auth := &coreauth.Auth{
		ID:       "codex-429-dedupe",
		FileName: "codex-429-dedupe.json",
		Provider: "codex",
		Status:   coreauth.StatusError,
		Attributes: map[string]string{
			"path": "codex-429-dedupe.json",
		},
		StatusMessage:  `{"error":{"type":"usage_limit_reached"}}`,
		Unavailable:    false,
		NextRetryAfter: nextRetry,
		LastError:      &coreauth.Error{Message: "usage limit reached", HTTPStatus: http.StatusTooManyRequests},
		Quota: coreauth.QuotaState{
			Exceeded:      true,
			Reason:        "quota",
			NextRecoverAt: nextRecover,
		},
		ModelStates: map[string]*coreauth.ModelState{
			"gpt-5.4-mini": makeModelState(),
			"gpt-5.5":      makeModelState(),
		},
	}

	entry := (&Handler{}).buildAuthFileEntry(auth)
	if entry == nil {
		t.Fatal("expected auth file entry")
	}
	restrictions, ok := entry["restrictions"].([]gin.H)
	if !ok {
		t.Fatalf("restrictions type = %T, want []gin.H", entry["restrictions"])
	}
	if len(restrictions) != 1 {
		t.Fatalf("restrictions length = %d, want 1", len(restrictions))
	}
	got := restrictions[0]
	if got["scope"] != "auth" || got["http_status"] != http.StatusTooManyRequests {
		t.Fatalf("restriction = %#v, want auth 429", got)
	}
	if _, hasModel := got["model"]; hasModel {
		t.Fatalf("restriction model = %#v, want no model field", got["model"])
	}
}

func TestBuildAuthFileEntryIncludesSubscriptionExpiration(t *testing.T) {
	expiresAt := time.Now().UTC().Add(90 * time.Minute).Truncate(time.Minute)
	auth := &coreauth.Auth{
		ID:       "codex-subscription",
		FileName: "codex-subscription.json",
		Provider: "codex",
		Attributes: map[string]string{
			"path": "codex-subscription.json",
		},
		Metadata: map[string]any{
			"subscription_expires_at": expiresAt.Format(time.RFC3339),
		},
	}

	entry := (&Handler{}).buildAuthFileEntry(auth)
	if entry == nil {
		t.Fatal("expected auth file entry")
	}
	if got, _ := entry["subscription_expires_at"].(string); got != expiresAt.Format(time.RFC3339) {
		t.Fatalf("subscription_expires_at = %q, want %q", got, expiresAt.Format(time.RFC3339))
	}
	if got, ok := entry["subscription_expires_at_ms"].(int64); !ok || got != expiresAt.UnixMilli() {
		t.Fatalf("subscription_expires_at_ms = %#v, want %d", entry["subscription_expires_at_ms"], expiresAt.UnixMilli())
	}
	if got, ok := entry["subscription_remaining_minutes"].(int64); !ok || got < 89 || got > 90 {
		t.Fatalf("subscription_remaining_minutes = %#v, want around 90", entry["subscription_remaining_minutes"])
	}
	if expired, _ := entry["subscription_expired"].(bool); expired {
		t.Fatal("subscription_expired = true, want false")
	}
}

func TestBuildAuthFileEntryIncludesSubscriptionStartAndPeriod(t *testing.T) {
	startedAt := time.Date(2026, 4, 1, 9, 30, 0, 0, time.UTC)
	auth := &coreauth.Auth{
		ID:       "codex-subscription-start",
		FileName: "codex-subscription-start.json",
		Provider: "codex",
		Attributes: map[string]string{
			"path": "codex-subscription-start.json",
		},
		Metadata: map[string]any{
			"subscription_started_at": startedAt.Format(time.RFC3339),
			"subscription_period":     "yearly",
		},
	}

	entry := (&Handler{}).buildAuthFileEntry(auth)
	if entry == nil {
		t.Fatal("expected auth file entry")
	}
	if got, _ := entry["subscription_started_at"].(string); got != startedAt.Format(time.RFC3339) {
		t.Fatalf("subscription_started_at = %q, want %q", got, startedAt.Format(time.RFC3339))
	}
	if got, ok := entry["subscription_started_at_ms"].(int64); !ok || got != startedAt.UnixMilli() {
		t.Fatalf("subscription_started_at_ms = %#v, want %d", entry["subscription_started_at_ms"], startedAt.UnixMilli())
	}
	if got, _ := entry["subscription_period"].(string); got != "yearly" {
		t.Fatalf("subscription_period = %q, want yearly", got)
	}
	wantExpiresAt := startedAt.AddDate(1, 0, 0)
	if got, _ := entry["subscription_expires_at"].(string); got != wantExpiresAt.Format(time.RFC3339) {
		t.Fatalf("subscription_expires_at = %q, want %q", got, wantExpiresAt.Format(time.RFC3339))
	}
	if got, ok := entry["subscription_expires_at_ms"].(int64); !ok || got != wantExpiresAt.UnixMilli() {
		t.Fatalf("subscription_expires_at_ms = %#v, want %d", entry["subscription_expires_at_ms"], wantExpiresAt.UnixMilli())
	}
	if _, ok := entry["subscription_remaining_days"].(int64); !ok {
		t.Fatalf("subscription_remaining_days = %#v, want int64", entry["subscription_remaining_days"])
	}
}

func TestClaudeOAuthMetadataFromTokenStorageIncludesRuntimeTokens(t *testing.T) {
	expired := time.Now().UTC().Add(time.Hour).Format(time.RFC3339)
	lastRefresh := time.Now().UTC().Add(-time.Minute).Format(time.RFC3339)

	meta := claudeOAuthMetadataFromTokenStorage(&claude.ClaudeTokenStorage{
		AccessToken:  "access-token",
		RefreshToken: "refresh-token",
		Email:        "claude@example.com",
		Expire:       expired,
		LastRefresh:  lastRefresh,
	})

	for key, want := range map[string]string{
		"type":          "claude",
		"access_token":  "access-token",
		"refresh_token": "refresh-token",
		"email":         "claude@example.com",
		"expired":       expired,
		"last_refresh":  lastRefresh,
	} {
		if got, _ := meta[key].(string); got != want {
			t.Fatalf("metadata[%s] = %q, want %q", key, got, want)
		}
	}
}

func TestPatchAuthFileFieldsUpdatesSubscriptionExpiration(t *testing.T) {
	gin.SetMode(gin.TestMode)

	store := &memoryAuthStore{}
	manager := coreauth.NewManager(store, nil, nil)
	_, err := manager.Register(context.Background(), &coreauth.Auth{
		ID:       "oauth-subscription",
		FileName: "oauth-subscription.json",
		Provider: "codex",
		Metadata: map[string]any{
			"email": "subscriber@example.com",
		},
	})
	if err != nil {
		t.Fatalf("register auth: %v", err)
	}

	h := &Handler{
		cfg:         &config.Config{},
		authManager: manager,
	}

	body, err := json.Marshal(map[string]any{
		"name":                    "oauth-subscription.json",
		"subscription_expires_at": "2027-01-02T03:04:00Z",
	})
	if err != nil {
		t.Fatalf("marshal body: %v", err)
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPatch, "/auth-files/fields", bytes.NewReader(body))
	c.Request.Header.Set("Content-Type", "application/json")

	h.PatchAuthFileFields(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected status %d, got %d, body=%s", http.StatusOK, rec.Code, rec.Body.String())
	}

	updated, ok := manager.GetByID("oauth-subscription")
	if !ok || updated == nil {
		t.Fatal("expected updated auth")
	}
	if got, _ := updated.Metadata["subscription_expires_at"].(string); got != "2027-01-02T03:04:00Z" {
		t.Fatalf("subscription_expires_at = %q, want %q", got, "2027-01-02T03:04:00Z")
	}
}

func TestPatchAuthFileFieldsUpdatesSubscriptionStartAndPeriod(t *testing.T) {
	gin.SetMode(gin.TestMode)

	store := &memoryAuthStore{}
	manager := coreauth.NewManager(store, nil, nil)
	_, err := manager.Register(context.Background(), &coreauth.Auth{
		ID:       "oauth-subscription-start",
		FileName: "oauth-subscription-start.json",
		Provider: "codex",
		Metadata: map[string]any{
			"email":                   "subscriber@example.com",
			"subscription_expires_at": "2099-01-01T00:00:00Z",
		},
	})
	if err != nil {
		t.Fatalf("register auth: %v", err)
	}

	h := &Handler{
		cfg:         &config.Config{},
		authManager: manager,
	}

	body, err := json.Marshal(map[string]any{
		"name":                    "oauth-subscription-start.json",
		"subscription_started_at": "2027-01-02T03:04:00Z",
		"subscription_period":     "yearly",
	})
	if err != nil {
		t.Fatalf("marshal body: %v", err)
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPatch, "/auth-files/fields", bytes.NewReader(body))
	c.Request.Header.Set("Content-Type", "application/json")

	h.PatchAuthFileFields(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected status %d, got %d, body=%s", http.StatusOK, rec.Code, rec.Body.String())
	}

	updated, ok := manager.GetByID("oauth-subscription-start")
	if !ok || updated == nil {
		t.Fatal("expected updated auth")
	}
	if got, _ := updated.Metadata["subscription_started_at"].(string); got != "2027-01-02T03:04:00Z" {
		t.Fatalf("subscription_started_at = %q, want %q", got, "2027-01-02T03:04:00Z")
	}
	if got, _ := updated.Metadata["subscription_period"].(string); got != "yearly" {
		t.Fatalf("subscription_period = %q, want yearly", got)
	}
	if _, exists := updated.Metadata["subscription_expires_at"]; exists {
		t.Fatalf("subscription_expires_at should be cleared, got %v", updated.Metadata["subscription_expires_at"])
	}
}

func TestPatchAuthFileFieldsClearsSubscriptionExpiration(t *testing.T) {
	gin.SetMode(gin.TestMode)

	store := &memoryAuthStore{}
	manager := coreauth.NewManager(store, nil, nil)
	_, err := manager.Register(context.Background(), &coreauth.Auth{
		ID:       "oauth-subscription-clear",
		FileName: "oauth-subscription-clear.json",
		Provider: "codex",
		Metadata: map[string]any{
			"email":                   "subscriber@example.com",
			"subscription_expires_at": "2027-01-02T03:04:00Z",
		},
	})
	if err != nil {
		t.Fatalf("register auth: %v", err)
	}

	h := &Handler{
		cfg:         &config.Config{},
		authManager: manager,
	}

	body, err := json.Marshal(map[string]any{
		"name":                    "oauth-subscription-clear.json",
		"subscription_expires_at": "",
	})
	if err != nil {
		t.Fatalf("marshal body: %v", err)
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPatch, "/auth-files/fields", bytes.NewReader(body))
	c.Request.Header.Set("Content-Type", "application/json")

	h.PatchAuthFileFields(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected status %d, got %d, body=%s", http.StatusOK, rec.Code, rec.Body.String())
	}

	updated, ok := manager.GetByID("oauth-subscription-clear")
	if !ok || updated == nil {
		t.Fatal("expected updated auth")
	}
	if _, exists := updated.Metadata["subscription_expires_at"]; exists {
		t.Fatalf("subscription_expires_at should be cleared, got %v", updated.Metadata["subscription_expires_at"])
	}
}

func TestPatchAuthFileFieldsRenamesRoutingChannelReferences(t *testing.T) {
	gin.SetMode(gin.TestMode)

	store := &memoryAuthStore{}
	manager := coreauth.NewManager(store, nil, nil)
	_, err := manager.Register(context.Background(), &coreauth.Auth{
		ID:       "oauth-auth-routing",
		FileName: "oauth-auth-routing.json",
		Provider: "claude",
		Label:    "Team Old",
		Metadata: map[string]any{
			"email": "team-old@example.com",
		},
	})
	if err != nil {
		t.Fatalf("register auth: %v", err)
	}

	h := &Handler{
		cfg: &config.Config{
			Routing: config.RoutingConfig{
				ChannelGroups: []config.RoutingChannelGroup{
					{
						Name: "team-alpha",
						Match: config.ChannelGroupMatch{
							Channels: []string{"Team Old", "Other Channel"},
						},
						ChannelPriorities: map[string]int{
							"Team Old":      80,
							"Other Channel": 10,
						},
					},
				},
			},
			OAuthModelAlias: map[string][]config.OAuthModelAlias{
				"team old": {{Name: "claude-sonnet", Alias: "sonnet"}},
			},
			SDKConfig: config.SDKConfig{
				APIKeyEntries: []config.APIKeyEntry{
					{Key: "sk-test", Name: "test", AllowedChannels: []string{"Team Old"}},
				},
			},
		},
		authManager: manager,
	}

	body, err := json.Marshal(map[string]any{
		"name":  "oauth-auth-routing.json",
		"label": "Team New",
	})
	if err != nil {
		t.Fatalf("marshal body: %v", err)
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPatch, "/auth-files/fields", bytes.NewReader(body))
	c.Request.Header.Set("Content-Type", "application/json")

	h.PatchAuthFileFields(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected status %d, got %d, body=%s", http.StatusOK, rec.Code, rec.Body.String())
	}
	group := h.cfg.Routing.ChannelGroups[0]
	if !containsString(group.Match.Channels, "Team New") {
		t.Fatalf("match.channels = %v, want renamed channel", group.Match.Channels)
	}
	if containsString(group.Match.Channels, "Team Old") {
		t.Fatalf("match.channels = %v, should not keep old channel", group.Match.Channels)
	}
	if got := group.ChannelPriorities["Team New"]; got != 80 {
		t.Fatalf("channel-priorities[Team New] = %d, want 80; map=%v", got, group.ChannelPriorities)
	}
	if _, exists := group.ChannelPriorities["Team Old"]; exists {
		t.Fatalf("channel-priorities = %v, should not keep old key", group.ChannelPriorities)
	}
	if _, exists := h.cfg.OAuthModelAlias["team old"]; exists {
		t.Fatalf("oauth-model-alias still has old channel: %v", h.cfg.OAuthModelAlias)
	}
	if _, exists := h.cfg.OAuthModelAlias["team new"]; !exists {
		t.Fatalf("oauth-model-alias missing new channel: %v", h.cfg.OAuthModelAlias)
	}
	if !containsString(h.cfg.APIKeyEntries[0].AllowedChannels, "Team New") {
		t.Fatalf("allowed-channels = %v, want renamed channel", h.cfg.APIKeyEntries[0].AllowedChannels)
	}
}

func TestPatchAuthFileFieldsRejectsDuplicateOAuthChannelLabel(t *testing.T) {
	gin.SetMode(gin.TestMode)

	store := &memoryAuthStore{}
	manager := coreauth.NewManager(store, nil, nil)
	_, err := manager.Register(context.Background(), &coreauth.Auth{
		ID:       "oauth-auth-2",
		FileName: "oauth-auth-2.json",
		Provider: "gemini",
		Metadata: map[string]any{
			"email": "oauth@example.com",
		},
	})
	if err != nil {
		t.Fatalf("register auth: %v", err)
	}

	h := &Handler{
		cfg: &config.Config{
			ClaudeKey: []config.ClaudeKey{
				{Name: "Shared Channel", APIKey: "claude-api-key"},
			},
		},
		authManager: manager,
	}

	body, err := json.Marshal(map[string]any{
		"name":  "oauth-auth-2.json",
		"label": "shared channel",
	})
	if err != nil {
		t.Fatalf("marshal body: %v", err)
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPatch, "/auth-files/fields", bytes.NewReader(body))
	c.Request.Header.Set("Content-Type", "application/json")

	h.PatchAuthFileFields(c)

	if rec.Code != http.StatusBadRequest {
		t.Fatalf("expected status %d, got %d, body=%s", http.StatusBadRequest, rec.Code, rec.Body.String())
	}

	updated, ok := manager.GetByID("oauth-auth-2")
	if !ok || updated == nil {
		t.Fatal("expected auth to remain registered")
	}
	if updated.Label != "" {
		t.Fatalf("label = %q, want empty", updated.Label)
	}
	if _, exists := updated.Metadata["label"]; exists {
		t.Fatalf("unexpected metadata label after rejected update: %v", updated.Metadata["label"])
	}
}

func TestPatchAuthFileFieldsAllowsRenameWhenUnrelatedOAuthAliasesConflict(t *testing.T) {
	gin.SetMode(gin.TestMode)

	store := &memoryAuthStore{}
	manager := coreauth.NewManager(store, nil, nil)
	for _, auth := range []*coreauth.Auth{
		{
			ID:       "oauth-alias-a",
			FileName: "oauth-alias-a.json",
			Provider: "codex",
			Label:    "Team A",
			Metadata: map[string]any{"email": "shared@example.com"},
		},
		{
			ID:       "oauth-alias-b",
			FileName: "oauth-alias-b.json",
			Provider: "codex",
			Label:    "Team B",
			Metadata: map[string]any{"email": "shared@example.com"},
		},
		{
			ID:       "oauth-auth-rename",
			FileName: "oauth-auth-rename.json",
			Provider: "claude",
			Label:    "Original Channel",
			Metadata: map[string]any{"email": "rename@example.com"},
		},
	} {
		if _, err := manager.Register(context.Background(), auth); err != nil {
			t.Fatalf("register auth %s: %v", auth.ID, err)
		}
	}

	h := &Handler{
		cfg:         &config.Config{},
		authManager: manager,
	}

	body, err := json.Marshal(map[string]any{
		"name":  "oauth-auth-rename.json",
		"label": "Renamed Channel",
	})
	if err != nil {
		t.Fatalf("marshal body: %v", err)
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPatch, "/auth-files/fields", bytes.NewReader(body))
	c.Request.Header.Set("Content-Type", "application/json")

	h.PatchAuthFileFields(c)

	if rec.Code != http.StatusOK {
		t.Fatalf("expected status %d, got %d, body=%s", http.StatusOK, rec.Code, rec.Body.String())
	}

	updated, ok := manager.GetByID("oauth-auth-rename")
	if !ok || updated == nil {
		t.Fatal("expected updated auth")
	}
	if updated.Label != "Renamed Channel" {
		t.Fatalf("label = %q, want %q", updated.Label, "Renamed Channel")
	}
}

func TestPatchAuthFileFieldsReturnsErrorWhenPersistenceFails(t *testing.T) {
	gin.SetMode(gin.TestMode)

	store := &memoryAuthStore{}
	manager := coreauth.NewManager(store, nil, nil)
	_, err := manager.Register(context.Background(), &coreauth.Auth{
		ID:       "oauth-auth-3",
		FileName: "oauth-auth-3.json",
		Provider: "codex",
		Metadata: map[string]any{
			"email": "persist@example.com",
		},
	})
	if err != nil {
		t.Fatalf("register auth: %v", err)
	}

	manager.SetStore(&failingAuthStore{
		items: map[string]*coreauth.Auth{
			"oauth-auth-3": {
				ID:        "oauth-auth-3",
				FileName:  "oauth-auth-3.json",
				Provider:  "codex",
				Metadata:  map[string]any{"email": "persist@example.com"},
				UpdatedAt: time.Now(),
			},
		},
	})

	h := &Handler{
		cfg:         &config.Config{},
		authManager: manager,
	}

	body, err := json.Marshal(map[string]any{
		"name":  "oauth-auth-3.json",
		"label": "Broken Persist",
	})
	if err != nil {
		t.Fatalf("marshal body: %v", err)
	}

	rec := httptest.NewRecorder()
	c, _ := gin.CreateTestContext(rec)
	c.Request = httptest.NewRequest(http.MethodPatch, "/auth-files/fields", bytes.NewReader(body))
	c.Request.Header.Set("Content-Type", "application/json")

	h.PatchAuthFileFields(c)

	if rec.Code != http.StatusInternalServerError {
		t.Fatalf("expected status %d, got %d, body=%s", http.StatusInternalServerError, rec.Code, rec.Body.String())
	}

	updated, ok := manager.GetByID("oauth-auth-3")
	if !ok || updated == nil {
		t.Fatal("expected auth to remain registered")
	}
	if updated.ChannelName() == "Broken Persist" {
		t.Fatalf("expected in-memory auth rollback on persist failure, got channel=%q", updated.ChannelName())
	}
}
