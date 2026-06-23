package management

import (
	"net/http"
	"testing"
)

func TestPanelUserAllowedGET(t *testing.T) {
	tests := []struct {
		path    string
		allowed bool
	}{
		{"/dashboard-summary", true},
		{"/usage/logs", true},
		{"/model-configs", true},
		{"/model-configs?scope=library", true},
		{"/model-owner-presets", true},
		{"/model-openrouter-sync", true},
		{"/model-pricing", true},
		{"/config", false},
		{"/management-key", false},
		{"/users", false},
		{"/auth-files", false},
	}
	for _, tc := range tests {
		if got := panelUserAllowedGET(tc.path); got != tc.allowed {
			t.Fatalf("panelUserAllowedGET(%q) = %v, want %v", tc.path, got, tc.allowed)
		}
	}
}

func TestPanelUserAllowedRequest(t *testing.T) {
	if !panelUserAllowedRequest(http.MethodGet, "/v0/management/usage", "/v0/management/usage") {
		t.Fatal("expected GET /usage to be allowed")
	}
	if panelUserAllowedRequest(http.MethodPut, "/v0/management/config", "/v0/management/config") {
		t.Fatal("expected PUT /config to be denied")
	}
	if !panelUserAllowedRequest(http.MethodPut, "/v0/management/auth/password", "/v0/management/auth/password") {
		t.Fatal("expected password change to be allowed")
	}
}
