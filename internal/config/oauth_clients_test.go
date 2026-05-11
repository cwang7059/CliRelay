package config

import "testing"

func TestGeminiOAuthClientCredentialsReturnsEmptyWithoutConfig(t *testing.T) {
	t.Setenv(EnvGeminiOAuthClientID, "")
	t.Setenv(EnvGeminiOAuthClientSecret, "")

	cfg := &Config{}

	clientID, clientSecret := cfg.OAuthClientCredentials(OAuthClientGemini)

	if clientID != "" {
		t.Fatalf("clientID = %q, want empty", clientID)
	}
	if clientSecret != "" {
		t.Fatalf("clientSecret = %q, want empty", clientSecret)
	}
}

func TestGeminiOAuthClientCredentialsKeepsExplicitConfig(t *testing.T) {
	t.Setenv(EnvGeminiOAuthClientID, "")
	t.Setenv(EnvGeminiOAuthClientSecret, "")

	cfg := &Config{
		OAuthClients: OAuthClients{
			Gemini: OAuthClient{
				ClientID:     "custom-client-id",
				ClientSecret: "custom-client-secret",
			},
		},
	}

	clientID, clientSecret := cfg.OAuthClientCredentials(OAuthClientGemini)

	if clientID != "custom-client-id" {
		t.Fatalf("clientID = %q, want custom-client-id", clientID)
	}
	if clientSecret != "custom-client-secret" {
		t.Fatalf("clientSecret = %q, want custom-client-secret", clientSecret)
	}
}

func TestGeminiOAuthClientCredentialsUsesEnvFallback(t *testing.T) {
	t.Setenv(EnvGeminiOAuthClientID, "env-client-id")
	t.Setenv(EnvGeminiOAuthClientSecret, "env-client-secret")

	cfg := &Config{}

	clientID, clientSecret := cfg.OAuthClientCredentials(OAuthClientGemini)

	if clientID != "env-client-id" {
		t.Fatalf("clientID = %q, want env-client-id", clientID)
	}
	if clientSecret != "env-client-secret" {
		t.Fatalf("clientSecret = %q, want env-client-secret", clientSecret)
	}
}

func TestAntigravityOAuthClientCredentialsUsesEnvFallback(t *testing.T) {
	t.Setenv(EnvAntigravityOAuthClientID, "env-antigravity-client-id")
	t.Setenv(EnvAntigravityOAuthClientSecret, "env-antigravity-client-secret")

	cfg := &Config{}

	clientID, clientSecret := cfg.OAuthClientCredentials(OAuthClientAntigravity)

	if clientID != "env-antigravity-client-id" {
		t.Fatalf("clientID = %q, want env-antigravity-client-id", clientID)
	}
	if clientSecret != "env-antigravity-client-secret" {
		t.Fatalf("clientSecret = %q, want env-antigravity-client-secret", clientSecret)
	}
}
