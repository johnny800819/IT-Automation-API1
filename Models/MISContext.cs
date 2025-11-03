using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace API.Models
{
    public partial class MISContext : DbContext
    {
        public MISContext()
        {
        }

        public MISContext(DbContextOptions<MISContext> options)
            : base(options)
        {
        }

        public virtual DbSet<LdapUserHistory> LdapUserHistory { get; set; }
        public virtual DbSet<LdapUserInfo> LdapUserInfo { get; set; }
        public virtual DbSet<LdapUserRole> LdapUserRole { get; set; }
        public virtual DbSet<VeeamBackupSessions> VeeamBackupSessions { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {

            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LdapUserHistory>(entity =>
            {
                entity.HasKey(e => e.sn)
                    .HasName("PK_LdapUserHistory_1");

                entity.Property(e => e.CommonName).HasMaxLength(30);

                entity.Property(e => e.Department).HasMaxLength(50);

                entity.Property(e => e.Email).HasMaxLength(50);

                entity.Property(e => e.IsActive).HasMaxLength(10);

                entity.Property(e => e.StreetAddress).HasMaxLength(50);

                entity.Property(e => e.UpdateTime).HasMaxLength(20);
            });

            modelBuilder.Entity<LdapUserInfo>(entity =>
            {
                entity.HasNoKey();

                entity.ToView("LdapUserInfo");

                entity.Property(e => e.CommonName).HasMaxLength(30);

                entity.Property(e => e.Department).HasMaxLength(50);

                entity.Property(e => e.Email).HasMaxLength(50);

                entity.Property(e => e.IsActive).HasMaxLength(10);

                entity.Property(e => e.StreetAddress).HasMaxLength(50);

                entity.Property(e => e.UpdateTime).HasMaxLength(20);
            });

            modelBuilder.Entity<LdapUserRole>(entity =>
            {
                entity.HasNoKey();

                entity.Property(e => e.basic_dn).HasMaxLength(100);

                entity.Property(e => e.role).HasMaxLength(100);
            });

            modelBuilder.Entity<VeeamBackupSessions>(entity =>
            {
                entity.HasNoKey();

                entity.Property(e => e.SessionEndTime).HasMaxLength(50);

                entity.Property(e => e.Status).HasMaxLength(20);

                entity.Property(e => e.UpdateTime).HasMaxLength(50);

                entity.Property(e => e.VeeamServer).HasMaxLength(20);

                entity.Property(e => e.VmsSessionName).HasMaxLength(100);
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
