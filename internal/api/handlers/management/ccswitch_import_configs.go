package management

import (
	"encoding/json"
	"net/http"
	"strings"

	"github.com/gin-gonic/gin"
	"github.com/router-for-me/CLIProxyAPI/v6/internal/usage"
)

type ccSwitchImportConfigRequest struct {
	ID                   string                    `json:"id"`
	ClientType           string                    `json:"client-type"`
	ProviderName         string                    `json:"provider-name"`
	Note                 string                    `json:"note"`
	Enabled              *bool                     `json:"enabled"`
	DefaultModel         string                    `json:"default-model"`
	ModelMappings        []usage.CcSwitchModelMappingRow `json:"model-mappings"`
	AllowedChannelGroups []string                        `json:"allowed-channel-groups"`
	RoutePath            string                          `json:"route-path"`
	EndpointPath         string                          `json:"endpoint-path"`
	UsageAutoInterval    int                             `json:"usage-auto-interval"`
	APIKeyField          string                          `json:"api-key-field"`
	CreatedAt            string                          `json:"created-at"`
	UpdatedAt            string                          `json:"updated-at"`
}

func (h *Handler) GetCcSwitchImportConfigs(c *gin.Context) {
	items := usage.ListCcSwitchImportConfigs()
	if items == nil {
		items = []usage.CcSwitchImportConfigRow{}
	}
	c.JSON(http.StatusOK, gin.H{
		"ccswitch-import-configs": items,
		"items":                   items,
	})
}

func (h *Handler) PutCcSwitchImportConfigs(c *gin.Context) {
	data, err := c.GetRawData()
	if err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": "failed to read body"})
		return
	}

	var requests []ccSwitchImportConfigRequest
	if err = json.Unmarshal(data, &requests); err != nil {
		var body struct {
			Items []ccSwitchImportConfigRequest `json:"items"`
		}
		if err2 := json.Unmarshal(data, &body); err2 != nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": "invalid body"})
			return
		}
		requests = body.Items
	}

	items := make([]usage.CcSwitchImportConfigRow, len(requests))
	for idx := range requests {
		request := requests[idx]
		items[idx] = usage.CcSwitchImportConfigRow{
			ID:                   strings.TrimSpace(request.ID),
			ClientType:           strings.ToLower(strings.TrimSpace(request.ClientType)),
			ProviderName:         strings.TrimSpace(request.ProviderName),
			Note:                 strings.TrimSpace(request.Note),
			Enabled:              request.Enabled == nil || *request.Enabled,
			DefaultModel:         strings.TrimSpace(request.DefaultModel),
			ModelMappings:        request.ModelMappings,
			AllowedChannelGroups: request.AllowedChannelGroups,
			RoutePath:            strings.TrimSpace(request.RoutePath),
			EndpointPath:         request.EndpointPath,
			UsageAutoInterval:    request.UsageAutoInterval,
			APIKeyField:          strings.TrimSpace(request.APIKeyField),
			CreatedAt:            strings.TrimSpace(request.CreatedAt),
			UpdatedAt:            strings.TrimSpace(request.UpdatedAt),
		}

		switch items[idx].ClientType {
		case "claude", "codex", "gemini":
		default:
			c.JSON(http.StatusBadRequest, gin.H{"error": "client-type must be one of claude, codex, gemini"})
			return
		}

		if items[idx].ID == "" {
			c.JSON(http.StatusBadRequest, gin.H{"error": "id is required"})
			return
		}
		if items[idx].ProviderName == "" {
			c.JSON(http.StatusBadRequest, gin.H{"error": "provider-name is required"})
			return
		}
		if items[idx].DefaultModel == "" {
			c.JSON(http.StatusBadRequest, gin.H{"error": "default-model is required"})
			return
		}
	}

	if err := usage.ReplaceAllCcSwitchImportConfigs(items); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
		return
	}

	c.JSON(http.StatusOK, gin.H{"status": "ok"})
}
