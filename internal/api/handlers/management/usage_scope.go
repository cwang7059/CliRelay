package management

import (
	"net/http"
	"strings"

	"github.com/gin-gonic/gin"
	"github.com/router-for-me/CLIProxyAPI/v6/internal/usage"
)

func (h *Handler) resolveUsageScope(c *gin.Context) (usage.APIKeyScope, error) {
	if isPanelAdmin(c) {
		return usage.OpenScope(), nil
	}
	userID := panelUserIDFromContext(c)
	if userID == "" {
		return usage.RestrictedScope(nil), nil
	}
	keys, err := usage.ListPanelUserAPIKeyIDs(userID)
	if err != nil {
		return usage.APIKeyScope{}, err
	}
	return usage.RestrictedScope(keys), nil
}

func (h *Handler) abortIfUsageScopeError(c *gin.Context, err error) bool {
	if err == nil {
		return false
	}
	if err == usage.ErrNoAPIKeyAccess {
		c.AbortWithStatusJSON(http.StatusForbidden, gin.H{"error": "permission denied"})
		return true
	}
	c.AbortWithStatusJSON(http.StatusInternalServerError, gin.H{"error": err.Error()})
	return true
}

func (h *Handler) buildScopedLogQueryParams(c *gin.Context, scope usage.APIKeyScope, base usage.LogQueryParams) (usage.LogQueryParams, bool) {
	params := base
	params.APIKey = strings.TrimSpace(params.APIKey)
	if err := scope.ApplyLogQueryParams(&params); err != nil {
		if h.abortIfUsageScopeError(c, err) {
			return usage.LogQueryParams{}, false
		}
	}
	return params, true
}

func (h *Handler) buildScopedChartQueryParams(c *gin.Context, scope usage.APIKeyScope, apiKey string, days int) (usage.LogQueryParams, bool) {
	params := usage.LogQueryParams{
		APIKey: strings.TrimSpace(apiKey),
		Days:   days,
	}
	if err := scope.ApplyLogQueryParams(&params); err != nil {
		if h.abortIfUsageScopeError(c, err) {
			return usage.LogQueryParams{}, false
		}
	}
	return params, true
}

func (h *Handler) ensureLogContentAccess(c *gin.Context, scope usage.APIKeyScope, logID int64) bool {
	if !scope.Restricted {
		return true
	}
	apiKey, err := usage.GetRequestLogAPIKey(logID)
	if err != nil {
		if strings.Contains(err.Error(), "no rows") {
			c.AbortWithStatusJSON(http.StatusNotFound, gin.H{"error": "log entry not found"})
			return false
		}
		c.AbortWithStatusJSON(http.StatusInternalServerError, gin.H{"error": err.Error()})
		return false
	}
	if !scope.Allows(apiKey) {
		c.AbortWithStatusJSON(http.StatusForbidden, gin.H{"error": "permission denied"})
		return false
	}
	return true
}

func filterUsageSnapshot(snapshot usage.StatisticsSnapshot, scope usage.APIKeyScope) usage.StatisticsSnapshot {
	if !scope.Restricted {
		return snapshot
	}
	filtered := usage.StatisticsSnapshot{
		APIs: make(map[string]usage.APISnapshot),
	}
	if len(scope.Keys) == 0 {
		return filtered
	}
	for _, key := range scope.Keys {
		data, ok := snapshot.APIs[key]
		if !ok {
			continue
		}
		filtered.APIs[key] = data
		filtered.TotalRequests += data.TotalRequests
		filtered.TotalTokens += data.TotalTokens
	}
	return filtered
}
