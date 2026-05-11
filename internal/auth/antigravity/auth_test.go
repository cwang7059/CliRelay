package antigravity

import (
	"net/url"
	"testing"

	"github.com/router-for-me/CLIProxyAPI/v6/internal/config"
)

func TestBuildAuthURLUsesConfiguredAntigravityClient(t *testing.T) {
	t.Setenv(config.EnvAntigravityOAuthClientID, "env-antigravity-client-id")
	t.Setenv(config.EnvAntigravityOAuthClientSecret, "env-antigravity-client-secret")

	auth := NewAntigravityAuth(&config.Config{}, nil)

	authURL := auth.BuildAuthURL("state-value", "http://localhost:51121/oauth-callback")
	if authURL == "" {
		t.Fatal("authURL is empty, want URL with configured Antigravity client")
	}

	parsed, err := url.Parse(authURL)
	if err != nil {
		t.Fatalf("parse authURL: %v", err)
	}
	if got := parsed.Query().Get("client_id"); got != "env-antigravity-client-id" {
		t.Fatalf("client_id = %q, want env-antigravity-client-id", got)
	}
	if got := parsed.Query().Get("state"); got != "state-value" {
		t.Fatalf("state = %q, want state-value", got)
	}
}
