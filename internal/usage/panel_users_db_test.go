package usage

import (
	"path/filepath"
	"testing"
	"time"

	"github.com/router-for-me/CLIProxyAPI/v6/internal/config"
)

func TestPanelUsersAuthFlow(t *testing.T) {
	dir := t.TempDir()
	dbPath := filepath.Join(dir, "usage.db")
	if err := InitDB(dbPath, config.RequestLogStorageConfig{}, time.UTC); err != nil {
		t.Fatalf("InitDB() error = %v", err)
	}
	t.Cleanup(CloseDB)

	user, err := CreatePanelUser("alice", "secret-pass", PanelRoleUser)
	if err != nil {
		t.Fatalf("CreatePanelUser() error = %v", err)
	}
	if user.Username != "alice" {
		t.Fatalf("username = %q, want alice", user.Username)
	}

	if _, err := AuthenticatePanelUser("alice", "wrong"); err == nil {
		t.Fatal("expected invalid credentials")
	}
	authUser, err := AuthenticatePanelUser("alice", "secret-pass")
	if err != nil {
		t.Fatalf("AuthenticatePanelUser() error = %v", err)
	}

	session, err := CreatePanelSession(authUser.ID, "127.0.0.1", "test")
	if err != nil {
		t.Fatalf("CreatePanelSession() error = %v", err)
	}
	if !IsPanelSessionToken(session.Token) {
		t.Fatalf("token %q should have panel prefix", session.Token)
	}

	_, loaded, ok := LookupPanelSession(session.Token)
	if !ok {
		t.Fatal("expected session lookup to succeed")
	}
	if loaded.Username != "alice" {
		t.Fatalf("loaded username = %q", loaded.Username)
	}

	if err := SetPanelUserAPIKeys(user.ID, []string{"sk-alice"}); err != nil {
		t.Fatalf("SetPanelUserAPIKeys() error = %v", err)
	}
	allowed, err := PanelUserAllowedAPIKey(PanelRoleUser, user.ID, "sk-alice")
	if err != nil || !allowed {
		t.Fatalf("expected user to own sk-alice, allowed=%v err=%v", allowed, err)
	}
	allowed, err = PanelUserAllowedAPIKey(PanelRoleUser, user.ID, "sk-other")
	if err != nil || allowed {
		t.Fatalf("expected user to not own sk-other, allowed=%v err=%v", allowed, err)
	}

	users, err := ListPanelUsers()
	if err != nil {
		t.Fatalf("ListPanelUsers() error = %v", err)
	}
	if len(users) != 1 {
		t.Fatalf("ListPanelUsers() len = %d, want 1", len(users))
	}
	if len(users[0].APIKeyIDs) != 1 || users[0].APIKeyIDs[0] != "sk-alice" {
		t.Fatalf("ListPanelUsers() api_key_ids = %v, want [sk-alice]", users[0].APIKeyIDs)
	}
}
