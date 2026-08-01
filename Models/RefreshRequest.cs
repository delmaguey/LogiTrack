using System.ComponentModel.DataAnnotations;

namespace LogiTrack.Models
{
    public class RefreshRequest
    {
        [Required]
        public required string RefreshToken { get; set; }
    }
}
