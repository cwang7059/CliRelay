package management

import (
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
)

const chromeAuthExtensionEnv = "CLIRELAY_CHROME_AUTH_EXTENSION_DIR"
const chromeAuthProfileEnv = "CLIRELAY_CHROME_AUTH_PROFILE_DIR"
const chromeAuthPathEnv = "CLIRELAY_CHROME_PATH"

func (h *Handler) openCodexRecoveryBrowser(authURL string) error {
	authURL = strings.TrimSpace(authURL)
	if authURL == "" {
		return fmt.Errorf("auth url is empty")
	}

	extensionDir, err := findChromeAuthExtensionDir()
	if err != nil {
		return err
	}
	chromePath, err := findChromeExecutable()
	if err != nil {
		return err
	}
	profileDir, err := chromeAuthProfileDir()
	if err != nil {
		return err
	}
	if err := os.MkdirAll(profileDir, 0o700); err != nil {
		return fmt.Errorf("create chrome auth profile: %w", err)
	}

	args := []string{
		"--user-data-dir=" + profileDir,
		"--no-first-run",
		"--no-default-browser-check",
		"--disable-default-apps",
		"--disable-extensions-except=" + extensionDir,
		"--load-extension=" + extensionDir,
		"--new-window",
		authURL,
	}
	cmd := exec.Command(chromePath, args...)
	if err := cmd.Start(); err != nil {
		return fmt.Errorf("start chrome auth helper: %w", err)
	}
	if err := cmd.Process.Release(); err != nil {
		return fmt.Errorf("release chrome auth helper process: %w", err)
	}
	return nil
}

func findChromeAuthExtensionDir() (string, error) {
	if dir := strings.TrimSpace(os.Getenv(chromeAuthExtensionEnv)); dir != "" {
		return validateChromeAuthExtensionDir(dir)
	}

	candidates := make([]string, 0, 6)
	if wd, err := os.Getwd(); err == nil && wd != "" {
		candidates = append(candidates,
			filepath.Join(wd, "tools", "chrome-auth-extension"),
			filepath.Join(wd, "..", "tools", "chrome-auth-extension"),
		)
	}
	if exe, err := os.Executable(); err == nil && exe != "" {
		exeDir := filepath.Dir(exe)
		candidates = append(candidates,
			filepath.Join(exeDir, "tools", "chrome-auth-extension"),
			filepath.Join(exeDir, "..", "tools", "chrome-auth-extension"),
			filepath.Join(exeDir, "..", "..", "tools", "chrome-auth-extension"),
		)
	}

	var checked []string
	for _, candidate := range candidates {
		dir, err := validateChromeAuthExtensionDir(candidate)
		if err == nil {
			return dir, nil
		}
		checked = append(checked, candidate)
	}
	return "", fmt.Errorf("chrome auth extension not found; set %s (checked %s)", chromeAuthExtensionEnv, strings.Join(checked, ", "))
}

func validateChromeAuthExtensionDir(dir string) (string, error) {
	clean := filepath.Clean(strings.TrimSpace(dir))
	if clean == "." || clean == "" {
		return "", errors.New("empty chrome auth extension dir")
	}
	manifest := filepath.Join(clean, "manifest.json")
	if stat, err := os.Stat(manifest); err != nil || stat.IsDir() {
		return "", fmt.Errorf("missing chrome extension manifest at %s", manifest)
	}
	return clean, nil
}

func chromeAuthProfileDir() (string, error) {
	if dir := strings.TrimSpace(os.Getenv(chromeAuthProfileEnv)); dir != "" {
		return filepath.Clean(dir), nil
	}
	base, err := os.UserCacheDir()
	if err != nil || strings.TrimSpace(base) == "" {
		base = os.TempDir()
	}
	if strings.TrimSpace(base) == "" {
		return "", errors.New("cache dir unavailable")
	}
	return filepath.Join(base, "CliRelay", "chrome-auth-helper-v2"), nil
}

func findChromeExecutable() (string, error) {
	if path := strings.TrimSpace(os.Getenv(chromeAuthPathEnv)); path != "" {
		if stat, err := os.Stat(path); err == nil && !stat.IsDir() {
			return path, nil
		}
		return "", fmt.Errorf("invalid %s: %s", chromeAuthPathEnv, path)
	}

	for _, candidate := range chromeExecutableCandidates() {
		if candidate == "" {
			continue
		}
		if strings.ContainsAny(candidate, `/\`) {
			if stat, err := os.Stat(candidate); err == nil && !stat.IsDir() {
				return candidate, nil
			}
			continue
		}
		if path, err := exec.LookPath(candidate); err == nil {
			return path, nil
		}
	}
	return "", fmt.Errorf("chrome executable not found; set %s", chromeAuthPathEnv)
}

func chromeExecutableCandidates() []string {
	switch runtime.GOOS {
	case "windows":
		return []string{
			filepath.Join(os.Getenv("ProgramFiles"), "Google", "Chrome", "Application", "chrome.exe"),
			filepath.Join(os.Getenv("ProgramFiles(x86)"), "Google", "Chrome", "Application", "chrome.exe"),
			filepath.Join(os.Getenv("LocalAppData"), "Google", "Chrome", "Application", "chrome.exe"),
			"chrome.exe",
		}
	case "darwin":
		return []string{
			"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
			filepath.Join(os.Getenv("HOME"), "Applications", "Google Chrome.app", "Contents", "MacOS", "Google Chrome"),
			"google-chrome",
			"chromium",
		}
	default:
		return []string{
			"google-chrome",
			"google-chrome-stable",
			"chromium",
			"chromium-browser",
		}
	}
}
