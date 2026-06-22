package management

import (
	"errors"
	"net/http"
	"strings"

	"github.com/gin-gonic/gin"
	"github.com/router-for-me/CLIProxyAPI/v6/internal/usage"
)

type panelCreateUserRequest struct {
	Username string   `json:"username"`
	Password string   `json:"password"`
	Role     string   `json:"role"`
	APIKeyIDs []string `json:"api_key_ids,omitempty"`
}

type panelUpdateUserRequest struct {
	Role     string `json:"role"`
	Disabled bool   `json:"disabled"`
}

type panelResetPasswordRequest struct {
	Password string `json:"password"`
}

type panelUserAPIKeysRequest struct {
	APIKeyIDs []string `json:"api_key_ids"`
}

func (h *Handler) GetPanelUsers(c *gin.Context) {
	if !isPanelAdmin(c) {
		c.JSON(http.StatusForbidden, gin.H{"error": "permission denied"})
		return
	}
	users, err := usage.ListPanelUsers()
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": err.Error()})
		return
	}
	c.JSON(http.StatusOK, gin.H{"users": users})
}

func (h *Handler) PostPanelUser(c *gin.Context) {
	if !isPanelAdmin(c) {
		c.JSON(http.StatusForbidden, gin.H{"error": "permission denied"})
		return
	}
	var req panelCreateUserRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid request body"})
		return
	}
	user, err := usage.CreatePanelUser(req.Username, req.Password, req.Role)
	if err != nil {
		status := http.StatusInternalServerError
		if errors.Is(err, usage.ErrPanelUserExists) {
			status = http.StatusConflict
		}
		c.JSON(status, gin.H{"error": err.Error()})
		return
	}
	if len(req.APIKeyIDs) > 0 {
		if err := usage.SetPanelUserAPIKeys(user.ID, req.APIKeyIDs); err != nil {
			c.JSON(http.StatusInternalServerError, gin.H{"error": err.Error()})
			return
		}
		user, _ = usage.GetPanelUserByID(user.ID)
	}
	c.JSON(http.StatusCreated, user)
}

func (h *Handler) PutPanelUser(c *gin.Context) {
	if !isPanelAdmin(c) {
		c.JSON(http.StatusForbidden, gin.H{"error": "permission denied"})
		return
	}
	id := strings.TrimSpace(c.Param("id"))
	var req panelUpdateUserRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid request body"})
		return
	}
	user, err := usage.UpdatePanelUser(id, req.Role, req.Disabled)
	if err != nil {
		status := http.StatusInternalServerError
		if errors.Is(err, usage.ErrPanelUserNotFound) {
			status = http.StatusNotFound
		}
		c.JSON(status, gin.H{"error": err.Error()})
		return
	}
	c.JSON(http.StatusOK, user)
}

func (h *Handler) DeletePanelUser(c *gin.Context) {
	if !isPanelAdmin(c) {
		c.JSON(http.StatusForbidden, gin.H{"error": "permission denied"})
		return
	}
	id := strings.TrimSpace(c.Param("id"))
	if id == panelUserIDFromContext(c) {
		c.JSON(http.StatusBadRequest, gin.H{"error": "cannot delete current user"})
		return
	}
	if err := usage.DeletePanelUser(id); err != nil {
		status := http.StatusInternalServerError
		if errors.Is(err, usage.ErrPanelUserNotFound) {
			status = http.StatusNotFound
		}
		c.JSON(status, gin.H{"error": err.Error()})
		return
	}
	c.JSON(http.StatusOK, gin.H{"status": "ok"})
}

func (h *Handler) PostPanelUserResetPassword(c *gin.Context) {
	if !isPanelAdmin(c) {
		c.JSON(http.StatusForbidden, gin.H{"error": "permission denied"})
		return
	}
	id := strings.TrimSpace(c.Param("id"))
	var req panelResetPasswordRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid request body"})
		return
	}
	if err := usage.UpdatePanelUserPassword(id, req.Password); err != nil {
		status := http.StatusInternalServerError
		if errors.Is(err, usage.ErrPanelUserNotFound) {
			status = http.StatusNotFound
		}
		c.JSON(status, gin.H{"error": err.Error()})
		return
	}
	c.JSON(http.StatusOK, gin.H{"status": "ok"})
}

func (h *Handler) PutPanelUserAPIKeys(c *gin.Context) {
	if !isPanelAdmin(c) {
		c.JSON(http.StatusForbidden, gin.H{"error": "permission denied"})
		return
	}
	id := strings.TrimSpace(c.Param("id"))
	var req panelUserAPIKeysRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "invalid request body"})
		return
	}
	if _, err := usage.GetPanelUserByID(id); err != nil {
		status := http.StatusNotFound
		if !errors.Is(err, usage.ErrPanelUserNotFound) {
			status = http.StatusInternalServerError
		}
		c.JSON(status, gin.H{"error": err.Error()})
		return
	}
	if err := usage.SetPanelUserAPIKeys(id, req.APIKeyIDs); err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": err.Error()})
		return
	}
	user, err := usage.GetPanelUserByID(id)
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": err.Error()})
		return
	}
	c.JSON(http.StatusOK, user)
}
