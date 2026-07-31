using LogiTrack.Models;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace LogiTrack.Models
{
    public class Order
    {
        [Key]
        public int OrderId { get; set; }
        [Required]
        public string? CustomerName { get; set; }
        [Required]
        [JsonConverter(typeof(CustomDateTimeConverter))]
        public DateTime DatePlaced { get; set; }
        public ICollection<InventoryItem> Items { get; set; } = new List<InventoryItem>();

        public void AddItem(InventoryItem item)
        {
            if (item is null)
            {
                return;
            }

            if (!Items.Contains(item))
            {
                if (item.Order != this)
                {
                    item.Order = this;
                    item.OrderId = OrderId;
                }

                Items.Add(item);
            }
        }

        public void RemoveItem(int itemId)
        {
            var item = Items.FirstOrDefault(x => x.Id == itemId);

            if (item != null)
            {
                item.Order = null;
                item.OrderId = null;
                Items.Remove(item);
            }
        }

        public string GetOrderSummary(int orderId)
        {
            var inventoryItem = Items.FirstOrDefault(x => x.Id == orderId);

            if (inventoryItem != null)
            {
                return $"Summary for Order {orderId}: Item: {inventoryItem.Name}, Quantity: {inventoryItem.Quantity}, Location: {inventoryItem.Location}";
            }

            return $"Not order found for Order {orderId}";
        }
    }

}
