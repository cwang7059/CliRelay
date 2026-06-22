package usage

import (
	"testing"
)

func TestAPIKeyScopeApplyLogQueryParams(t *testing.T) {
	scope := RestrictedScope([]string{"sk-alice", "sk-bob"})

	params := LogQueryParams{Days: 7}
	if err := scope.ApplyLogQueryParams(&params); err != nil {
		t.Fatalf("ApplyLogQueryParams() error = %v", err)
	}
	if len(params.APIKeys) != 2 {
		t.Fatalf("APIKeys len = %d, want 2", len(params.APIKeys))
	}

	params = LogQueryParams{Days: 7, APIKey: "sk-alice"}
	if err := scope.ApplyLogQueryParams(&params); err != nil {
		t.Fatalf("ApplyLogQueryParams() allowed key error = %v", err)
	}

	params = LogQueryParams{Days: 7, APIKey: "sk-other"}
	if err := scope.ApplyLogQueryParams(&params); err != ErrNoAPIKeyAccess {
		t.Fatalf("ApplyLogQueryParams() error = %v, want ErrNoAPIKeyAccess", err)
	}

	emptyScope := RestrictedScope(nil)
	params = LogQueryParams{Days: 7}
	if err := emptyScope.ApplyLogQueryParams(&params); err != nil {
		t.Fatalf("ApplyLogQueryParams() empty scope error = %v", err)
	}
	if !params.MatchNone {
		t.Fatal("expected MatchNone for user without bound keys")
	}
}

func TestBuildWhereClauseScopesAPIKeys(t *testing.T) {
	where, args := buildWhereClause(LogQueryParams{
		Days:    7,
		APIKeys: []string{"sk-a", "sk-b"},
	})
	if where == "" {
		t.Fatal("expected non-empty where clause")
	}
	if len(args) != 3 {
		t.Fatalf("args len = %d, want 3", len(args))
	}
}

func TestBuildWhereClauseMatchNone(t *testing.T) {
	where, args := buildWhereClause(LogQueryParams{MatchNone: true})
	if where != " WHERE 1 = 0" {
		t.Fatalf("where = %q, want forced empty result", where)
	}
	if len(args) != 0 {
		t.Fatalf("args len = %d, want 0", len(args))
	}
}
