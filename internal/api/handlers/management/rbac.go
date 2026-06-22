package management

import (
	"net/http"
	"strings"

	"github.com/gin-gonic/gin"
	"github.com/router-for-me/CLIProxyAPI/v6/internal/usage"
)

const (
	ctxPanelRole     = "panel_role"
	ctxPanelUserID   = "panel_user_id"
	ctxPanelUsername = "panel_username"
	ctxPanelAuthMode = "panel_auth_mode"
)

const (
	panelAuthModeSession = "session"
	panelAuthModeLegacy  = "legacy"
)

func setPanelContext(c *gin.Context, role, userID, username, authMode string) {
	c.Set(ctxPanelRole, role)
	c.Set(ctxPanelUserID, userID)
	c.Set(ctxPanelUsername, username)
	c.Set(ctxPanelAuthMode, authMode)
}

func panelRoleFromContext(c *gin.Context) string {
	role, _ := c.Get(ctxPanelRole)
	value, _ := role.(string)
	if strings.TrimSpace(value) == "" {
		return usage.PanelRoleAdmin
	}
	return value
}

func panelUserIDFromContext(c *gin.Context) string {
	value, _ := c.Get(ctxPanelUserID)
	id, _ := value.(string)
	return strings.TrimSpace(id)
}

func isPanelAdmin(c *gin.Context) bool {
	return panelRoleFromContext(c) == usage.PanelRoleAdmin
}

// RBACMiddleware enforces route-level permissions for panel users.
func (h *Handler) RBACMiddleware() gin.HandlerFunc {
	return func(c *gin.Context) {
		if isPanelAdmin(c) {
			c.Next()
			return
		}
		if panelUserAllowedRequest(c.Request.Method, c.FullPath(), c.Request.URL.Path) {
			c.Next()
			return
		}
		c.AbortWithStatusJSON(http.StatusForbidden, gin.H{"error": "permission denied"})
	}
}

func panelUserAllowedRequest(method, fullPath, requestPath string) bool {
	method = strings.ToUpper(strings.TrimSpace(method))
	if method == http.MethodPost && strings.HasSuffix(requestPath, "/auth/logout") {
		return true
	}
	if method == http.MethodPut && strings.HasSuffix(requestPath, "/auth/password") {
		return true
	}
	if method != http.MethodGet {
		return false
	}

	candidates := []string{
		strings.TrimSpace(fullPath),
		normalizeRBACPath(requestPath),
	}
	for _, path := range candidates {
		if path == "" {
			continue
		}
		if panelUserAllowedGET(path) {
			return true
		}
	}
	return false
}

func normalizeRBACPath(path string) string {
	path = strings.TrimSpace(path)
	if !strings.HasPrefix(path, "/") {
		path = "/" + path
	}
	if idx := strings.Index(path, "/v0/management"); idx >= 0 {
		path = path[idx+len("/v0/management"):]
	}
	if path == "" {
		path = "/"
	}
	return path
}

func panelUserAllowedGET(path string) bool {
	path = normalizeRBACPath(path)
	allowedPrefixes := []string{
		"/auth/me",
		"/dashboard-summary",
		"/system-stats",
		"/usage",
		"/logs",
		"/request-error-logs",
		"/request-log",
		"/request-log-by-id/",
		"/models",
		"/model-configs",
		"/channel-groups",
		"/api-key-entries",
		"/api-keys",
		"/latest-version",
	}
	for _, prefix := range allowedPrefixes {
		if path == prefix || strings.HasPrefix(path, prefix) {
			return true
		}
	}
	return false
}
