package management

import (
	"errors"
	"net/http"
	"strings"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/router-for-me/CLIProxyAPI/v6/internal/usage"
)

type panelLoginRequest struct {
	Username string `json:"username"`
	Password string `json:"password"`
}

type panelPasswordRequest struct {
	CurrentPassword string `json:"current_password"`
	NewPassword     string `json:"new_password"`
}

type panelBootstrapRequest struct {
	Username string `json:"username"`
	Password string `json:"password"`
}

func (h *Handler) PostPanelLogin(c *gin.Context) {
	var req panelLoginRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid request body"})
		return
	}
	user, err := usage.AuthenticatePanelUser(req.Username, req.Password)
	if err != nil {
		switch {
		case errors.Is(err, usage.ErrPanelInvalidCredentials):
			h.recordPanelAuthFailure(c)
			c.JSON(http.StatusUnauthorized, gin.H{"error": "invalid username or password"})
		case errors.Is(err, usage.ErrPanelUserDisabled):
			c.JSON(http.StatusForbidden, gin.H{"error": "user disabled"})
		default:
			c.JSON(http.StatusInternalServerError, gin.H{"error": err.Error()})
		}
		return
	}
	h.clearPanelAuthFailures(c)
	session, err := usage.CreatePanelSession(user.ID, c.ClientIP(), c.Request.UserAgent())
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to create session"})
		return
	}
	c.JSON(http.StatusOK, gin.H{
		"token":      session.Token,
		"role":       user.Role,
		"username":   user.Username,
		"expires_at": session.ExpiresAt.UTC().Format(time.RFC3339),
	})
}

func (h *Handler) PostPanelLogout(c *gin.Context) {
	token := extractProvidedCredential(c)
	if usage.IsPanelSessionToken(token) {
		_ = usage.DeletePanelSession(token)
	}
	c.JSON(http.StatusOK, gin.H{"status": "ok"})
}

func (h *Handler) GetPanelMe(c *gin.Context) {
	role := panelRoleFromContext(c)
	username, _ := c.Get(ctxPanelUsername)
	name, _ := username.(string)
	userID := panelUserIDFromContext(c)
	authMode, _ := c.Get(ctxPanelAuthMode)
	mode, _ := authMode.(string)
	c.JSON(http.StatusOK, gin.H{
		"username":    name,
		"role":        role,
		"user_id":     userID,
		"auth_mode":   mode,
		"permissions": panelPermissionsForRole(role),
	})
}

func (h *Handler) PutPanelPassword(c *gin.Context) {
	if isPanelAdmin(c) && panelUserIDFromContext(c) == "" {
		c.JSON(http.StatusForbidden, gin.H{"error": "legacy management key cannot change password here"})
		return
	}
	var req panelPasswordRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid request body"})
		return
	}
	userID := panelUserIDFromContext(c)
	if userID == "" {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "session required"})
		return
	}
	user, err := usage.GetPanelUserByID(userID)
	if err != nil {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "user not found"})
		return
	}
	current, err := usage.AuthenticatePanelUser(user.Username, req.CurrentPassword)
	if err != nil || current.ID != user.ID {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "invalid current password"})
		return
	}
	if strings.TrimSpace(req.NewPassword) == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "new password is required"})
		return
	}
	if err := usage.UpdatePanelUserPassword(userID, req.NewPassword); err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": err.Error()})
		return
	}
	c.JSON(http.StatusOK, gin.H{"status": "ok"})
}

// PostPanelBootstrap creates the first admin user when no panel users exist.
// Requires a valid legacy management key in the Authorization header.
func (h *Handler) PostPanelBootstrap(c *gin.Context) {
	count, err := usage.PanelUserCount()
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": err.Error()})
		return
	}
	if count > 0 {
		c.JSON(http.StatusForbidden, gin.H{"error": "bootstrap not allowed"})
		return
	}
	if !h.authenticateLegacyManagementKey(c) {
		c.JSON(http.StatusUnauthorized, gin.H{"error": "management key required"})
		return
	}
	var req panelBootstrapRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid request body"})
		return
	}
	user, err := usage.CreatePanelUser(req.Username, req.Password, usage.PanelRoleAdmin)
	if err != nil {
		status := http.StatusInternalServerError
		if errors.Is(err, usage.ErrPanelUserExists) {
			status = http.StatusConflict
		}
		c.JSON(status, gin.H{"error": err.Error()})
		return
	}
	c.JSON(http.StatusCreated, gin.H{
		"id":       user.ID,
		"username": user.Username,
		"role":     user.Role,
	})
}

func panelPermissionsForRole(role string) []string {
	if role == usage.PanelRoleAdmin {
		return []string{"admin:*"}
	}
	return []string{
		"observe:read",
		"resources:read:models",
		"security:read:own_keys",
		"profile:write",
	}
}

func extractProvidedCredential(c *gin.Context) string {
	if ah := c.GetHeader("Authorization"); ah != "" {
		parts := strings.SplitN(ah, " ", 2)
		if len(parts) == 2 && strings.EqualFold(parts[0], "bearer") {
			return strings.TrimSpace(parts[1])
		}
		return strings.TrimSpace(ah)
	}
	if key := strings.TrimSpace(c.GetHeader("X-Management-Key")); key != "" {
		return key
	}
	return strings.TrimSpace(c.Query("token"))
}

func (h *Handler) recordPanelAuthFailure(c *gin.Context) {
	clientIP := c.ClientIP()
	if clientIP == "127.0.0.1" || clientIP == "::1" {
		return
	}
	h.attemptsMu.Lock()
	defer h.attemptsMu.Unlock()
	ai := h.failedAttempts[clientIP]
	if ai == nil {
		ai = &attemptInfo{}
		h.failedAttempts[clientIP] = ai
	}
	ai.count++
	ai.lastActivity = time.Now()
	if ai.count >= 5 {
		ai.blockedUntil = time.Now().Add(30 * time.Minute)
		ai.count = 0
	}
}

func (h *Handler) clearPanelAuthFailures(c *gin.Context) {
	clientIP := c.ClientIP()
	if clientIP == "127.0.0.1" || clientIP == "::1" {
		return
	}
	h.attemptsMu.Lock()
	defer h.attemptsMu.Unlock()
	if ai := h.failedAttempts[clientIP]; ai != nil {
		ai.count = 0
		ai.blockedUntil = time.Time{}
	}
}
