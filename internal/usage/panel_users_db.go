package usage

import (
	"crypto/rand"
	"database/sql"
	"encoding/hex"
	"errors"
	"fmt"
	"strings"
	"time"

	"github.com/google/uuid"
	log "github.com/sirupsen/logrus"
	"golang.org/x/crypto/bcrypt"
)

const (
	PanelRoleAdmin = "admin"
	PanelRoleUser  = "user"

	panelSessionTokenPrefix = "panel_"
	panelSessionTTL         = 24 * time.Hour
	panelBcryptCost         = 12
)

var (
	ErrPanelUserNotFound      = errors.New("panel user not found")
	ErrPanelUserExists        = errors.New("panel username already exists")
	ErrPanelInvalidCredentials = errors.New("invalid username or password")
	ErrPanelUserDisabled      = errors.New("panel user disabled")
	ErrPanelSessionNotFound   = errors.New("panel session not found")
	ErrPanelBootstrapNotAllowed = errors.New("panel bootstrap not allowed")
)

// PanelUser represents a management panel operator account.
type PanelUser struct {
	ID           string `json:"id"`
	Username     string `json:"username"`
	Role         string `json:"role"`
	Disabled     bool   `json:"disabled"`
	CreatedAt    string `json:"created_at"`
	UpdatedAt    string `json:"updated_at"`
	LastLoginAt  string `json:"last_login_at,omitempty"`
	APIKeyIDs    []string `json:"api_key_ids,omitempty"`
}

// PanelSession represents an authenticated panel session.
type PanelSession struct {
	Token     string
	UserID    string
	ExpiresAt time.Time
	CreatedAt time.Time
	IP        string
	UserAgent string
}

const createPanelUsersTableSQL = `
CREATE TABLE IF NOT EXISTS panel_users (
  id            TEXT PRIMARY KEY NOT NULL,
  username      TEXT NOT NULL UNIQUE,
  password_hash TEXT NOT NULL,
  role          TEXT NOT NULL CHECK(role IN ('admin', 'user')),
  disabled      INTEGER NOT NULL DEFAULT 0,
  created_at    TEXT NOT NULL,
  updated_at    TEXT NOT NULL,
  last_login_at TEXT NOT NULL DEFAULT ''
);
`

const createPanelSessionsTableSQL = `
CREATE TABLE IF NOT EXISTS panel_sessions (
  token       TEXT PRIMARY KEY NOT NULL,
  user_id     TEXT NOT NULL,
  expires_at  TEXT NOT NULL,
  created_at  TEXT NOT NULL,
  ip          TEXT NOT NULL DEFAULT '',
  user_agent  TEXT NOT NULL DEFAULT '',
  FOREIGN KEY(user_id) REFERENCES panel_users(id) ON DELETE CASCADE
);
`

const createPanelUserAPIKeysTableSQL = `
CREATE TABLE IF NOT EXISTS panel_user_api_keys (
  user_id     TEXT NOT NULL,
  api_key_id  TEXT NOT NULL,
  PRIMARY KEY (user_id, api_key_id),
  FOREIGN KEY(user_id) REFERENCES panel_users(id) ON DELETE CASCADE
);
`

func initPanelUsersTables(db *sql.DB) {
	if db == nil {
		return
	}
	for _, stmt := range []string{
		createPanelUsersTableSQL,
		createPanelSessionsTableSQL,
		createPanelUserAPIKeysTableSQL,
	} {
		if _, err := db.Exec(stmt); err != nil {
			log.Errorf("usage: create panel users table: %v", err)
		}
	}
}

func panelNowRFC3339() string {
	return time.Now().UTC().Format(time.RFC3339)
}

func hashPanelPassword(password string) (string, error) {
	hash, err := bcrypt.GenerateFromPassword([]byte(password), panelBcryptCost)
	if err != nil {
		return "", err
	}
	return string(hash), nil
}

func checkPanelPassword(hash, password string) bool {
	if strings.TrimSpace(hash) == "" || strings.TrimSpace(password) == "" {
		return false
	}
	return bcrypt.CompareHashAndPassword([]byte(hash), []byte(password)) == nil
}

func normalizePanelUsername(username string) string {
	return strings.TrimSpace(username)
}

func normalizePanelRole(role string) string {
	role = strings.TrimSpace(strings.ToLower(role))
	if role == PanelRoleAdmin {
		return PanelRoleAdmin
	}
	return PanelRoleUser
}

// PanelUserCount returns the number of panel users.
func PanelUserCount() (int, error) {
	db := getDB()
	if db == nil {
		return 0, errors.New("usage database not initialized")
	}
	var count int
	if err := db.QueryRow(`SELECT COUNT(*) FROM panel_users`).Scan(&count); err != nil {
		return 0, err
	}
	return count, nil
}

func scanPanelUser(row interface {
	Scan(dest ...any) error
}) (PanelUser, error) {
	var user PanelUser
	var disabled int
	var lastLogin sql.NullString
	if err := row.Scan(
		&user.ID,
		&user.Username,
		&user.Role,
		&disabled,
		&user.CreatedAt,
		&user.UpdatedAt,
		&lastLogin,
	); err != nil {
		return PanelUser{}, err
	}
	user.Disabled = disabled != 0
	if lastLogin.Valid {
		user.LastLoginAt = lastLogin.String
	}
	return user, nil
}

// CreatePanelUser inserts a new panel user.
func CreatePanelUser(username, password, role string) (PanelUser, error) {
	db := getDB()
	if db == nil {
		return PanelUser{}, errors.New("usage database not initialized")
	}
	username = normalizePanelUsername(username)
	if username == "" {
		return PanelUser{}, fmt.Errorf("username is required")
	}
	if strings.TrimSpace(password) == "" {
		return PanelUser{}, fmt.Errorf("password is required")
	}
	role = normalizePanelRole(role)

	passwordHash, err := hashPanelPassword(password)
	if err != nil {
		return PanelUser{}, err
	}

	now := panelNowRFC3339()
	user := PanelUser{
		ID:        uuid.NewString(),
		Username:  username,
		Role:      role,
		CreatedAt: now,
		UpdatedAt: now,
	}
	_, err = db.Exec(
		`INSERT INTO panel_users (id, username, password_hash, role, disabled, created_at, updated_at, last_login_at)
		 VALUES (?, ?, ?, ?, 0, ?, ?, '')`,
		user.ID, user.Username, passwordHash, user.Role, user.CreatedAt, user.UpdatedAt,
	)
	if err != nil {
		if strings.Contains(strings.ToLower(err.Error()), "unique") {
			return PanelUser{}, ErrPanelUserExists
		}
		return PanelUser{}, err
	}
	return user, nil
}

// AuthenticatePanelUser validates credentials and returns the user.
func AuthenticatePanelUser(username, password string) (PanelUser, error) {
	user, err := GetPanelUserByUsername(username)
	if err != nil {
		return PanelUser{}, ErrPanelInvalidCredentials
	}
	if user.Disabled {
		return PanelUser{}, ErrPanelUserDisabled
	}
	db := getDB()
	if db == nil {
		return PanelUser{}, errors.New("usage database not initialized")
	}
	var passwordHash string
	if err := db.QueryRow(`SELECT password_hash FROM panel_users WHERE id = ?`, user.ID).Scan(&passwordHash); err != nil {
		return PanelUser{}, ErrPanelInvalidCredentials
	}
	if !checkPanelPassword(passwordHash, password) {
		return PanelUser{}, ErrPanelInvalidCredentials
	}
	return user, nil
}

// GetPanelUserByUsername loads a user by username.
func GetPanelUserByUsername(username string) (PanelUser, error) {
	db := getDB()
	if db == nil {
		return PanelUser{}, errors.New("usage database not initialized")
	}
	username = normalizePanelUsername(username)
	row := db.QueryRow(
		`SELECT id, username, role, disabled, created_at, updated_at, last_login_at
		 FROM panel_users WHERE username = ?`,
		username,
	)
	user, err := scanPanelUser(row)
	if err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			return PanelUser{}, ErrPanelUserNotFound
		}
		return PanelUser{}, err
	}
	return user, nil
}

// GetPanelUserByID loads a user by id.
func GetPanelUserByID(id string) (PanelUser, error) {
	db := getDB()
	if db == nil {
		return PanelUser{}, errors.New("usage database not initialized")
	}
	row := db.QueryRow(
		`SELECT id, username, role, disabled, created_at, updated_at, last_login_at
		 FROM panel_users WHERE id = ?`,
		strings.TrimSpace(id),
	)
	user, err := scanPanelUser(row)
	if err != nil {
		if errors.Is(err, sql.ErrNoRows) {
			return PanelUser{}, ErrPanelUserNotFound
		}
		return PanelUser{}, err
	}
	apiKeys, err := ListPanelUserAPIKeyIDs(user.ID)
	if err == nil {
		user.APIKeyIDs = apiKeys
	}
	return user, nil
}

// ListPanelUsers returns all panel users.
func ListPanelUsers() ([]PanelUser, error) {
	db := getDB()
	if db == nil {
		return nil, errors.New("usage database not initialized")
	}
	rows, err := db.Query(
		`SELECT id, username, role, disabled, created_at, updated_at, last_login_at
		 FROM panel_users ORDER BY username COLLATE NOCASE`,
	)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	users := make([]PanelUser, 0)
	for rows.Next() {
		user, err := scanPanelUser(rows)
		if err != nil {
			return nil, err
		}
		apiKeys, listErr := ListPanelUserAPIKeyIDs(user.ID)
		if listErr == nil {
			user.APIKeyIDs = apiKeys
		}
		users = append(users, user)
	}
	return users, rows.Err()
}

// UpdatePanelUser updates role/disabled state.
func UpdatePanelUser(id, role string, disabled bool) (PanelUser, error) {
	db := getDB()
	if db == nil {
		return PanelUser{}, errors.New("usage database not initialized")
	}
	role = normalizePanelRole(role)
	now := panelNowRFC3339()
	res, err := db.Exec(
		`UPDATE panel_users SET role = ?, disabled = ?, updated_at = ? WHERE id = ?`,
		role, boolToInt(disabled), now, strings.TrimSpace(id),
	)
	if err != nil {
		return PanelUser{}, err
	}
	affected, _ := res.RowsAffected()
	if affected == 0 {
		return PanelUser{}, ErrPanelUserNotFound
	}
	return GetPanelUserByID(id)
}

// DeletePanelUser removes a panel user and related sessions.
func DeletePanelUser(id string) error {
	db := getDB()
	if db == nil {
		return errors.New("usage database not initialized")
	}
	res, err := db.Exec(`DELETE FROM panel_users WHERE id = ?`, strings.TrimSpace(id))
	if err != nil {
		return err
	}
	affected, _ := res.RowsAffected()
	if affected == 0 {
		return ErrPanelUserNotFound
	}
	return nil
}

// UpdatePanelUserPassword changes a user's password.
func UpdatePanelUserPassword(id, newPassword string) error {
	db := getDB()
	if db == nil {
		return errors.New("usage database not initialized")
	}
	if strings.TrimSpace(newPassword) == "" {
		return fmt.Errorf("password is required")
	}
	passwordHash, err := hashPanelPassword(newPassword)
	if err != nil {
		return err
	}
	now := panelNowRFC3339()
	res, err := db.Exec(
		`UPDATE panel_users SET password_hash = ?, updated_at = ? WHERE id = ?`,
		passwordHash, now, strings.TrimSpace(id),
	)
	if err != nil {
		return err
	}
	affected, _ := res.RowsAffected()
	if affected == 0 {
		return ErrPanelUserNotFound
	}
	return nil
}

// CreatePanelSession issues a new session token for a user.
func CreatePanelSession(userID, ip, userAgent string) (PanelSession, error) {
	db := getDB()
	if db == nil {
		return PanelSession{}, errors.New("usage database not initialized")
	}
	token, err := newPanelSessionToken()
	if err != nil {
		return PanelSession{}, err
	}
	now := time.Now().UTC()
	session := PanelSession{
		Token:     token,
		UserID:    strings.TrimSpace(userID),
		ExpiresAt: now.Add(panelSessionTTL),
		CreatedAt: now,
		IP:        strings.TrimSpace(ip),
		UserAgent: strings.TrimSpace(userAgent),
	}
	_, err = db.Exec(
		`INSERT INTO panel_sessions (token, user_id, expires_at, created_at, ip, user_agent)
		 VALUES (?, ?, ?, ?, ?, ?)`,
		session.Token,
		session.UserID,
		session.ExpiresAt.Format(time.RFC3339),
		session.CreatedAt.Format(time.RFC3339),
		session.IP,
		session.UserAgent,
	)
	if err != nil {
		return PanelSession{}, err
	}
	_, _ = db.Exec(`UPDATE panel_users SET last_login_at = ?, updated_at = ? WHERE id = ?`,
		session.CreatedAt.Format(time.RFC3339), session.CreatedAt.Format(time.RFC3339), session.UserID)
	return session, nil
}

func newPanelSessionToken() (string, error) {
	buf := make([]byte, 32)
	if _, err := rand.Read(buf); err != nil {
		return "", err
	}
	return panelSessionTokenPrefix + hex.EncodeToString(buf), nil
}

// LookupPanelSession resolves a session token to session + user.
func LookupPanelSession(token string) (PanelSession, PanelUser, bool) {
	db := getDB()
	if db == nil || strings.TrimSpace(token) == "" {
		return PanelSession{}, PanelUser{}, false
	}
	var session PanelSession
	var expiresAt, createdAt string
	if err := db.QueryRow(
		`SELECT token, user_id, expires_at, created_at, ip, user_agent
		 FROM panel_sessions WHERE token = ?`,
		token,
	).Scan(&session.Token, &session.UserID, &expiresAt, &createdAt, &session.IP, &session.UserAgent); err != nil {
		return PanelSession{}, PanelUser{}, false
	}
	session.ExpiresAt, _ = time.Parse(time.RFC3339, expiresAt)
	session.CreatedAt, _ = time.Parse(time.RFC3339, createdAt)
	if session.ExpiresAt.IsZero() || time.Now().UTC().After(session.ExpiresAt) {
		_ = DeletePanelSession(token)
		return PanelSession{}, PanelUser{}, false
	}
	user, err := GetPanelUserByID(session.UserID)
	if err != nil || user.Disabled {
		_ = DeletePanelSession(token)
		return PanelSession{}, PanelUser{}, false
	}
	return session, user, true
}

// DeletePanelSession removes a session token.
func DeletePanelSession(token string) error {
	db := getDB()
	if db == nil {
		return errors.New("usage database not initialized")
	}
	_, err := db.Exec(`DELETE FROM panel_sessions WHERE token = ?`, strings.TrimSpace(token))
	return err
}

// IsPanelSessionToken reports whether a credential looks like a panel session token.
func IsPanelSessionToken(token string) bool {
	return strings.HasPrefix(strings.TrimSpace(token), panelSessionTokenPrefix)
}

// SetPanelUserAPIKeys replaces API key bindings for a user.
func SetPanelUserAPIKeys(userID string, apiKeyIDs []string) error {
	db := getDB()
	if db == nil {
		return errors.New("usage database not initialized")
	}
	userID = strings.TrimSpace(userID)
	if userID == "" {
		return fmt.Errorf("user id is required")
	}
	tx, err := db.Begin()
	if err != nil {
		return err
	}
	defer func() { _ = tx.Rollback() }()

	if _, err := tx.Exec(`DELETE FROM panel_user_api_keys WHERE user_id = ?`, userID); err != nil {
		return err
	}
	for _, keyID := range apiKeyIDs {
		keyID = strings.TrimSpace(keyID)
		if keyID == "" {
			continue
		}
		if _, err := tx.Exec(
			`INSERT INTO panel_user_api_keys (user_id, api_key_id) VALUES (?, ?)`,
			userID, keyID,
		); err != nil {
			return err
		}
		if _, err := tx.Exec(
			`UPDATE api_keys SET owner_user_id = ? WHERE key = ?`,
			userID, keyID,
		); err != nil {
			return err
		}
	}
	return tx.Commit()
}

// ListPanelUserAPIKeyIDs returns API key ids bound to a user.
func ListPanelUserAPIKeyIDs(userID string) ([]string, error) {
	db := getDB()
	if db == nil {
		return nil, errors.New("usage database not initialized")
	}
	rows, err := db.Query(`SELECT api_key_id FROM panel_user_api_keys WHERE user_id = ? ORDER BY api_key_id`, strings.TrimSpace(userID))
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	ids := make([]string, 0)
	for rows.Next() {
		var id string
		if err := rows.Scan(&id); err != nil {
			return nil, err
		}
		ids = append(ids, id)
	}
	return ids, rows.Err()
}

// PanelUserOwnsAPIKey reports whether a user is bound to the given API key id.
func PanelUserOwnsAPIKey(userID, apiKeyID string) (bool, error) {
	db := getDB()
	if db == nil {
		return false, errors.New("usage database not initialized")
	}
	var count int
	err := db.QueryRow(
		`SELECT COUNT(*) FROM panel_user_api_keys WHERE user_id = ? AND api_key_id = ?`,
		strings.TrimSpace(userID), strings.TrimSpace(apiKeyID),
	).Scan(&count)
	return count > 0, err
}

// PanelUserAllowedAPIKey reports whether a user may access the given API key value/id.
// Admins always return true. Users must have an explicit binding.
func PanelUserAllowedAPIKey(role, userID, apiKey string) (bool, error) {
	if normalizePanelRole(role) == PanelRoleAdmin {
		return true, nil
	}
	apiKey = strings.TrimSpace(apiKey)
	if apiKey == "" {
		return false, nil
	}
	ids, err := ListPanelUserAPIKeyIDs(userID)
	if err != nil {
		return false, err
	}
	for _, id := range ids {
		if id == apiKey {
			return true, nil
		}
	}
	return false, nil
}
