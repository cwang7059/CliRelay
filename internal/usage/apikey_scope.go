package usage

import (
	"errors"
	"strings"
)

var ErrNoAPIKeyAccess = errors.New("api key access denied")

// APIKeyScope controls optional API-key filtering for usage queries.
// Restricted=false means no extra filter (admin).
// Restricted=true with empty Keys returns no rows.
type APIKeyScope struct {
	Restricted bool
	Keys       []string
}

// OpenScope returns a scope that does not restrict usage queries.
func OpenScope() APIKeyScope {
	return APIKeyScope{}
}

// RestrictedScope returns a scope limited to the provided API keys.
func RestrictedScope(keys []string) APIKeyScope {
	normalized := make([]string, 0, len(keys))
	seen := make(map[string]struct{}, len(keys))
	for _, key := range keys {
		key = strings.TrimSpace(key)
		if key == "" {
			continue
		}
		if _, ok := seen[key]; ok {
			continue
		}
		seen[key] = struct{}{}
		normalized = append(normalized, key)
	}
	return APIKeyScope{Restricted: true, Keys: normalized}
}

// Allows reports whether the scope permits access to the given API key value.
func (s APIKeyScope) Allows(apiKey string) bool {
	if !s.Restricted {
		return true
	}
	apiKey = strings.TrimSpace(apiKey)
	if apiKey == "" {
		return false
	}
	for _, key := range s.Keys {
		if key == apiKey {
			return true
		}
	}
	return false
}

// ApplyLogQueryParams enforces the scope on log query parameters.
func (s APIKeyScope) ApplyLogQueryParams(params *LogQueryParams) error {
	if params == nil {
		return nil
	}
	if !s.Restricted {
		return nil
	}
	if len(s.Keys) == 0 {
		params.MatchNone = true
		params.APIKeys = nil
		return nil
	}

	requested := strings.TrimSpace(params.APIKey)
	if requested == "" {
		params.APIKeys = append([]string(nil), s.Keys...)
		return nil
	}
	if requested == systemRequestLogFilterValue {
		return ErrNoAPIKeyAccess
	}
	if !s.Allows(requested) {
		return ErrNoAPIKeyAccess
	}
	return nil
}

func appendAPIKeyINClause(conditions []string, args []interface{}, keys []string) ([]string, []interface{}) {
	placeholders := make([]string, 0, len(keys))
	for _, key := range keys {
		placeholders = append(placeholders, "?")
		args = append(args, key)
	}
	conditions = append(conditions, "api_key IN ("+strings.Join(placeholders, ",")+")")
	return conditions, args
}

func appendScopedAPIKeyConditions(conditions []string, args []interface{}, params LogQueryParams) ([]string, []interface{}) {
	if params.MatchNone {
		return append(conditions, "1 = 0"), args
	}
	if len(params.APIKeys) > 0 {
		return appendAPIKeyINClause(conditions, args, params.APIKeys)
	}
	return conditions, args
}

func appendScopeSQL(where string, args []interface{}, scope APIKeyScope) (string, []interface{}) {
	if !scope.Restricted {
		return where, args
	}
	if len(scope.Keys) == 0 {
		if where == "" {
			return " WHERE 1=0", args
		}
		return where + " AND 1=0", args
	}

	placeholders := make([]string, 0, len(scope.Keys))
	for _, key := range scope.Keys {
		placeholders = append(placeholders, "?")
		args = append(args, key)
	}
	clause := "api_key IN (" + strings.Join(placeholders, ",") + ")"
	if where == "" {
		return " WHERE " + clause, args
	}
	return where + " AND " + clause, args
}

func filterStrings(values []string, allowed map[string]struct{}) []string {
	if len(allowed) == 0 {
		return []string{}
	}
	filtered := make([]string, 0, len(values))
	for _, value := range values {
		if _, ok := allowed[value]; ok {
			filtered = append(filtered, value)
		}
	}
	return filtered
}
