package thinking

import "testing"

func TestParseMultiplierSuffix(t *testing.T) {
	tests := []struct {
		name   string
		raw    string
		want   ThinkingLevel
		wantOK bool
	}{
		{name: "fastest aliases map to low", raw: "2x", want: LevelLow, wantOK: true},
		{name: "normal maps to medium", raw: "1x", want: LevelMedium, wantOK: true},
		{name: "half speed maps to high", raw: "0.5x", want: LevelHigh, wantOK: true},
		{name: "quarter speed maps to xhigh", raw: "0.25x", want: LevelXHigh, wantOK: true},
		{name: "speed prefix is accepted", raw: "speed=2x", want: LevelLow, wantOK: true},
		{name: "multiplier prefix is accepted", raw: "multiplier:0.25x", want: LevelXHigh, wantOK: true},
		{name: "unknown value is rejected", raw: "8x", wantOK: false},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			got, ok := ParseMultiplierSuffix(tt.raw)
			if ok != tt.wantOK {
				t.Fatalf("ParseMultiplierSuffix(%q) ok = %v, want %v", tt.raw, ok, tt.wantOK)
			}
			if got != tt.want {
				t.Fatalf("ParseMultiplierSuffix(%q) = %q, want %q", tt.raw, got, tt.want)
			}
		})
	}
}

func TestParseSuffixToConfigMultiplierAliases(t *testing.T) {
	tests := []struct {
		raw  string
		want ThinkingLevel
	}{
		{raw: "2x", want: LevelLow},
		{raw: "1x", want: LevelMedium},
		{raw: "0.5x", want: LevelHigh},
		{raw: "0.25x", want: LevelXHigh},
	}

	for _, tt := range tests {
		t.Run(tt.raw, func(t *testing.T) {
			got := parseSuffixToConfig(tt.raw, "codex", "gpt-5.5("+tt.raw+")")
			if got.Mode != ModeLevel {
				t.Fatalf("Mode = %v, want %v", got.Mode, ModeLevel)
			}
			if got.Level != tt.want {
				t.Fatalf("Level = %q, want %q", got.Level, tt.want)
			}
		})
	}
}

func TestParseSuffixToConfigMultiplierDoesNotReplaceNumericBudget(t *testing.T) {
	got := parseSuffixToConfig("2048", "gemini", "gemini-2.5-pro(2048)")
	if got.Mode != ModeBudget {
		t.Fatalf("Mode = %v, want %v", got.Mode, ModeBudget)
	}
	if got.Budget != 2048 {
		t.Fatalf("Budget = %d, want 2048", got.Budget)
	}
}
