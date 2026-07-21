using System.ComponentModel.DataAnnotations;

namespace LogiTrack.Models
{
    public class InventoryItem
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string Name { get; set; } = String.Empty;
        [Required]
        public int Quantity { get; set; }
        [Required]
        public string  Location { get; set; } = String.Empty;


        public void DisplayInfo()
        {
            Console.WriteLine($"Item: {Name}, Quantity: {Quantity}, Location: {Location}");
        }
    }
}