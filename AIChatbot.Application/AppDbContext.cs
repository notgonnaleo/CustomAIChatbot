using AIChatbot.Application.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIChatbot.Application
{
    public class AppDbContext : DbContext
    {
        public AppDbContext()
        {
        }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {            
        }

        public DbSet<TextEmbedding> Embeddings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<TextEmbedding>(entity =>
            {
                entity.ToTable("TextEmbeddings", "dbo");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Message)
                    .HasMaxLength(4000);
                entity.Property(e => e.Embedding)
                    .HasColumnType("VARBINARY(MAX)");
            });
        }
    }
}
