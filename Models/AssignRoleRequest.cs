using System.ComponentModel.DataAnnotations;

namespace LogiTrack.Models
{
    public class AssignRoleRequest
    {
        [Required]
        [EmailAddress]
        public required string Email { get; set; }
    }
}
