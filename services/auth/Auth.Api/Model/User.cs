namespace Auth.Api.Model;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class User
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(255)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(255)]
    public string Password { get; set; } = string.Empty;
    
    [Required]
    [Column(TypeName = "varchar(50)")]
    public UserRole Role { get; set; } = UserRole.Employee;
    
    [Column("is_active")]
    public bool IsActive { get; set; } = true;
    
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
    
    [Column("last_login_at")]
    public DateTime? LastLoginAt { get; set; }

    // Id of the administrator who created this account via POST /api/auth/users.
    // Null for accounts created through public self-registration.
    [Column("created_by")]
    public int? CreatedBy { get; set; }

    // Password Reset & Security
    [MaxLength(255)]
    [Column("reset_token_hash")]
    public string? ResetTokenHash { get; set; }
    
    [Column("reset_token_expires_at_utc")]
    public DateTime? ResetTokenExpiresAtUtc { get; set; }
    
    [Column("reset_token_used")]
    public bool ResetTokenUsed { get; set; } = false;
    
    [Column("last_password_reset_requested_at_utc")]
    public DateTime? LastPasswordResetRequestedAtUtc { get; set; }
    
    [Column("reset_request_count_in_window")]
    public int ResetRequestCountInWindow { get; set; } = 0;
    
    [MaxLength(255)]
    [Column("security_stamp")]
    public string? SecurityStamp { get; set; } = Guid.NewGuid().ToString();
}