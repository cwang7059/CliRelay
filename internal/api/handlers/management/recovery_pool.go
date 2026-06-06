package management

import (
	"encoding/json"
	"io"
	"net/http"
	"regexp"
	"strings"

	"github.com/gin-gonic/gin"
	coreauth "github.com/router-for-me/CLIProxyAPI/v6/sdk/cliproxy/auth"
)

type MailApiResponse struct {
	Body       string `json:"body"`
	From       string `json:"from"`
	ReceivedAt string `json:"received_at"`
	Status     string `json:"status"`
	Subject    string `json:"subject"`
}

type RecoveryQueueItem struct {
	ID          string `json:"id"`
	Email       string `json:"email"`
	Phone       string `json:"phone,omitempty"`
	AccountID   string `json:"account_id,omitempty"`
	Status      string `json:"status"`
	HasPassword bool   `json:"has_password"`
	HasPhone    bool   `json:"has_phone"`
	HasMailbox  bool   `json:"has_mailbox"`
	LastError   string `json:"last_error,omitempty"`
}

// GetRecoveryQueue returns the list of 401 disabled accounts and their recovery status.
func (h *Handler) GetRecoveryQueue(c *gin.Context) {
	if h.authManager == nil {
		c.JSON(http.StatusOK, []RecoveryQueueItem{})
		return
	}

	queue := make([]RecoveryQueueItem, 0)
	items := h.authManager.List()
	for _, auth := range items {
		if !isCodex401Recoverable(auth) {
			continue
		}

		errStr := ""
		if auth.LastError != nil {
			errStr = auth.LastError.Message
		}

		queue = append(queue, RecoveryQueueItem{
			ID:          auth.ID,
			Email:       authEmail(auth),
			Phone:       authPhone(auth),
			AccountID:   strings.TrimSpace(codexAccountIDFromMetadata(auth.Metadata)),
			Status:      "waiting",
			HasPassword: authPassword(auth) != "",
			HasPhone:    authPhone(auth) != "",
			HasMailbox:  authHasMailbox(auth),
			LastError:   errStr,
		})
	}

	c.JSON(http.StatusOK, queue)
}

// FetchOTP attempts to fetch the latest OTP code from the mailapi_url for the given email.
func (h *Handler) FetchOTP(c *gin.Context) {
	email := strings.TrimSpace(c.Query("email"))
	if email == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "email required"})
		return
	}

	if h.authManager == nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "auth manager unavailable"})
		return
	}

	var targetAuth *coreauth.Auth
	for _, auth := range h.authManager.List() {
		if strings.EqualFold(authEmail(auth), email) {
			targetAuth = auth
			break
		}
	}

	if targetAuth == nil {
		c.JSON(http.StatusNotFound, gin.H{"error": "auth not found"})
		return
	}

	mailApiUrl := authMailboxURL(targetAuth)
	if mailApiUrl == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "no mailbox config for this auth"})
		return
	}

	resp, err := http.Get(mailApiUrl)
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to fetch mail: " + err.Error()})
		return
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to read mail response: " + err.Error()})
		return
	}

	var mailResp MailApiResponse
	if err := json.Unmarshal(body, &mailResp); err != nil {
		c.JSON(http.StatusInternalServerError, gin.H{"error": "failed to unmarshal mail: " + err.Error()})
		return
	}

	if mailResp.Body == "" {
		c.JSON(http.StatusNotFound, gin.H{"error": "mail body empty"})
		return
	}

	// Extract 6-digit code
	re := regexp.MustCompile(`\b\d{6}\b`)
	match := re.FindString(mailResp.Body)
	if match == "" {
		c.JSON(http.StatusNotFound, gin.H{"error": "no 6-digit code found in email"})
		return
	}

	c.JSON(http.StatusOK, gin.H{"code": match})
}

func authHasMailbox(auth *coreauth.Auth) bool {
	return authMailboxURL(auth) != ""
}

func authMailboxURL(auth *coreauth.Auth) string {
	if auth == nil {
		return ""
	}
	if mailApiURL := mailboxURLFromAnyMap(auth.Mailbox); mailApiURL != "" {
		return mailApiURL
	}
	if mailApiURL := mailboxURLFromAny(auth.Metadata["mailbox"]); mailApiURL != "" {
		return mailApiURL
	}
	for _, key := range []string{"mailapi_url", "mailbox_url"} {
		if raw, ok := auth.Metadata[key].(string); ok {
			if value := strings.TrimSpace(raw); value != "" {
				return value
			}
		}
	}
	return ""
}

func mailboxURLFromAny(value any) string {
	switch typed := value.(type) {
	case map[string]any:
		return mailboxURLFromAnyMap(typed)
	case map[string]string:
		return strings.TrimSpace(typed["mailapi_url"])
	default:
		return ""
	}
}

func mailboxURLFromAnyMap(values map[string]any) string {
	if values == nil {
		return ""
	}
	raw, ok := values["mailapi_url"].(string)
	if !ok {
		return ""
	}
	return strings.TrimSpace(raw)
}
