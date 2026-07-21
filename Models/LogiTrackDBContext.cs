using Microsoft.EntityFrameworkCore;

namespace LogiTrack.Models
{
    public class LogiTrackDBContext : DbContext
    {
        public DbSet<InventoryItem> InventoryItems { get; set; }
        public DbSet<Order> Orders { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite("Data Source=logitrack.db");

        public LogiTrackDBContext(DbContextOptions<LogiTrackDBContext> options) : base(options)
        {
        }
    }
}