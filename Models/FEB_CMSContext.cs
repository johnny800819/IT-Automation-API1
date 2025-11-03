using System;
using System.Collections.Generic;
using API.Models.FEB_CMS;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace API.Models
{
    public partial class FEB_CMSContext : DbContext
    {
        public FEB_CMSContext()
        {
        }

        public FEB_CMSContext(DbContextOptions<FEB_CMSContext> options)
            : base(options)
        {
        }

        public virtual DbSet<User> User { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {

        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.Property(e => e.Account)
                    .IsRequired()
                    .HasMaxLength(50)
                    .IsUnicode(false);

                entity.Property(e => e.CreateTime).HasDefaultValueSql("(getdate())");

                entity.Property(e => e.Department).HasMaxLength(30);

                entity.Property(e => e.Email).HasMaxLength(50);

                entity.Property(e => e.EmployeeId).HasMaxLength(30);

                entity.Property(e => e.JobTitle).HasMaxLength(30);

                entity.Property(e => e.JurisdictionJson)
                    .HasMaxLength(2000)
                    .IsUnicode(false);

                entity.Property(e => e.Name).HasMaxLength(200);

                entity.Property(e => e.Status).HasDefaultValueSql("((1))");

                entity.Property(e => e.UpdateTime).HasDefaultValueSql("(getdate())");

                entity.Property(e => e.UserPassword)
                    .HasMaxLength(20)
                    .IsUnicode(false);

                entity.Property(e => e.ValidateDate).HasColumnType("date");
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
