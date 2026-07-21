using LogiTrack.Models;
using System.ComponentModel.DataAnnotations;

namespace LogiTrack.Models
{
    public class Order
    {
        [Key]
        public int OrderId { get; set; }
        [Required]
        public string? CustomerName { get; set; }
        [Required]
        public DateTime DatePlaced { get; set; }
        public List<InventoryItem>? Items { get; set; } //= new List<InventoryItem>();


        public void AddItem(InventoryItem item)
        {
            if (Items == null)
            {
                Items = new List<InventoryItem>();
            }
            Items.Add(item);
        }

        public void RemoveItem(int itemId)
        {
            if (Items != null)
            {
                var item = Items.Find(x=> x.Id == itemId);

                if (item != null)
                {
                    Items.Remove(item);
                }
            }
        }

        public string GetOrderSummary(int orderId)
        {
            if (Items != null)
            {
                var inventoryItem = Items.Find(x=> x.Id == orderId);

                if (inventoryItem != null)
                {
                    return $"Summary for Order {orderId}: Item: {inventoryItem.Name}, Quantity: {inventoryItem.Quantity}, Location: {inventoryItem.Location}";
                }
            }

            return $"Not order found for Order {orderId}";
        }
    }

}
