namespace LogiTrack.Models
{
    public class RefreshToken
    {
        public int Id { get; set; }
        public required string UserId { get; set; }

        // Stores a SHA-256 hash of the token, never the plaintext value, so a database leak alone
        // doesn't hand out usable credentials.
        public required string TokenHash { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }

        // Set when this token was rotated out for a newer one. If a client ever presents a token
        // that already has this set, that token was already used once before - a strong signal of
        // token theft/replay, not just an expired session.
        public string? ReplacedByTokenHash { get; set; }

        public bool IsActive => RevokedAt == null && DateTime.UtcNow < ExpiresAt;
    }
}
