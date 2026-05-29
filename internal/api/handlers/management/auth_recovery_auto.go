package management

import (
	"context"
	"strings"
	"time"

	coreauth "github.com/router-for-me/CLIProxyAPI/v6/sdk/cliproxy/auth"
	log "github.com/sirupsen/logrus"
)

// HandleUnauthorizedAuth starts an automatic Codex OAuth recovery flow after a
// credential has been disabled by a 401/permanent-auth failure.
func (h *Handler) HandleUnauthorizedAuth(ctx context.Context, auth *coreauth.Auth, result coreauth.Result) {
	if h == nil || auth == nil {
		return
	}
	if h.cfg != nil && !h.cfg.AutoRecoverCodex401 {
		return
	}
	if !strings.EqualFold(strings.TrimSpace(auth.Provider), "codex") {
		return
	}
	if !isCodex401Recoverable(auth) {
		return
	}
	recovery := recoveryContextFromAuth(auth)
	if recovery == nil {
		return
	}
	if !h.beginAutoCodex401Recovery(recovery.TargetID) {
		return
	}

	if ctx == nil {
		ctx = context.Background()
	}
	resultLabel := strings.TrimSpace(result.Model)
	if resultLabel == "" {
		resultLabel = "auth"
	}
	log.WithFields(log.Fields{
		"auth_id": recovery.TargetID,
		"email":   recovery.TargetEmail,
		"model":   resultLabel,
	}).Info("starting automatic Codex 401 OAuth recovery")

	started, err := h.startCodexOAuthFlow(ctx, codexOAuthStartOptions{
		Recovery:             recovery,
		UseCallbackForwarder: true,
		OpenBrowser:          true,
	})
	if err != nil {
		h.finishAutoCodex401Recovery(recovery.TargetID)
		log.WithError(err).WithField("auth_id", recovery.TargetID).Warn("failed to start automatic Codex 401 recovery")
		return
	}
	if started != nil && started.OpenError != "" {
		log.WithFields(log.Fields{
			"auth_id": recovery.TargetID,
			"error":   started.OpenError,
		}).Warn("automatic Codex 401 recovery started but browser open failed")
		return
	}
	fields := log.Fields{"auth_id": recovery.TargetID}
	if started != nil {
		fields["state"] = started.State
		fields["opened"] = started.Opened
	}
	log.WithFields(fields).Info("automatic Codex 401 recovery OAuth flow started")
}

func (h *Handler) beginAutoCodex401Recovery(authID string) bool {
	authID = strings.TrimSpace(authID)
	if h == nil || authID == "" {
		return false
	}
	now := time.Now()
	h.autoRecoveryMu.Lock()
	defer h.autoRecoveryMu.Unlock()
	if h.autoRecoveryInFlight == nil {
		h.autoRecoveryInFlight = make(map[string]time.Time)
	}
	for id, startedAt := range h.autoRecoveryInFlight {
		if now.Sub(startedAt) > autoCodex401RecoveryCooldown {
			delete(h.autoRecoveryInFlight, id)
		}
	}
	if _, ok := h.autoRecoveryInFlight[autoCodex401RecoveryGlobalKey]; ok {
		return false
	}
	if startedAt, ok := h.autoRecoveryInFlight[authID]; ok && now.Sub(startedAt) <= autoCodex401RecoveryCooldown {
		return false
	}
	h.autoRecoveryInFlight[authID] = now
	h.autoRecoveryInFlight[autoCodex401RecoveryGlobalKey] = now
	return true
}

func (h *Handler) finishAutoCodex401Recovery(authID string) {
	authID = strings.TrimSpace(authID)
	if h == nil || authID == "" {
		return
	}
	h.autoRecoveryMu.Lock()
	delete(h.autoRecoveryInFlight, authID)
	delete(h.autoRecoveryInFlight, autoCodex401RecoveryGlobalKey)
	h.autoRecoveryMu.Unlock()
}
