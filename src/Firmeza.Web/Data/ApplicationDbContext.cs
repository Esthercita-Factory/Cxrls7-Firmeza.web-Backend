using Firmeza.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Firmeza.Web.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleDetail> SaleDetails => Set<SaleDetail>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Product>(entity =>
        {
            entity.HasIndex(p => p.Sku).IsUnique();
            entity.Property(p => p.UnitPrice).HasPrecision(12, 2);
            entity.Property(p => p.Sku).IsRequired();
        });

        builder.Entity<Customer>(entity =>
        {
            entity.HasIndex(c => c.DocumentNumber).IsUnique();
            entity.HasIndex(c => c.Email).IsUnique();
        });

        builder.Entity<Sale>(entity =>
        {
            entity.HasIndex(s => s.ExternalReference).IsUnique();
            entity.Property(s => s.Subtotal).HasPrecision(14, 2);
            entity.Property(s => s.TaxRate).HasPrecision(5, 4);
            entity.Property(s => s.TaxAmount).HasPrecision(14, 2);
            entity.Property(s => s.Total).HasPrecision(14, 2);
            entity.HasOne(s => s.Customer).WithMany(c => c.Sales)
                .HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SaleDetail>(entity =>
        {
            entity.Property(d => d.UnitPrice).HasPrecision(12, 2);
            entity.Property(d => d.LineTotal).HasPrecision(14, 2);
            entity.HasOne(d => d.Sale).WithMany(s => s.Details)
                .HasForeignKey(d => d.SaleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(d => d.Product).WithMany(p => p.SaleDetails)
                .HasForeignKey(d => d.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.Customer).WithMany(c => c.Users)
            .HasForeignKey(u => u.CustomerId).OnDelete(DeleteBehavior.SetNull);
    }
}
