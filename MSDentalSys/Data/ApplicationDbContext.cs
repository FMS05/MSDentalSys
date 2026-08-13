using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Models;

namespace MSDentalSys.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Paciente> Pacientes { get; set; }
        public DbSet<AntecedenteClinico> AntecedentesClinicos { get; set; }
        public DbSet<ServicioOdontologico> ServiciosOdontologicos { get; set; }
        public DbSet<Cita> Citas { get; set; }
        public DbSet<AtencionOdontologica> AtencionesOdontologicas { get; set; }
        public DbSet<Diagnostico> Diagnosticos { get; set; }
        public DbSet<EvolucionClinica> EvolucionesClinicas { get; set; }
        public DbSet<Tratamiento> Tratamientos { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Paciente 1:1 AntecedenteClinico
            builder.Entity<Paciente>()
                .HasOne(p => p.AntecedenteClinico)
                .WithOne(a => a.Paciente)
                .HasForeignKey<AntecedenteClinico>(a => a.PacienteId)
                .OnDelete(DeleteBehavior.Cascade);

            // Cédula opcional pero única cuando exista
            builder.Entity<Paciente>()
                .HasIndex(p => p.Cedula)
                .IsUnique()
                .HasFilter("[Cedula] IS NOT NULL");

            // Paciente 1:N Cita
            builder.Entity<Cita>()
                .HasOne(c => c.Paciente)
                .WithMany()
                .HasForeignKey(c => c.PacienteId)
                .OnDelete(DeleteBehavior.Restrict);

            // Odontólogo 1:N Cita
            builder.Entity<Cita>()
                .HasOne(c => c.Odontologo)
                .WithMany()
                .HasForeignKey(c => c.OdontologoId)
                .OnDelete(DeleteBehavior.Restrict);

            // Servicio 1:N Cita
            builder.Entity<Cita>()
                .HasOne(c => c.ServicioOdontologico)
                .WithMany(s => s.Citas)
                .HasForeignKey(c => c.ServicioOdontologicoId)
                .OnDelete(DeleteBehavior.Restrict);

            // Paciente 1:N Atención
            builder.Entity<AtencionOdontologica>()
                .HasOne(a => a.Paciente)
                .WithMany()
                .HasForeignKey(a => a.PacienteId)
                .OnDelete(DeleteBehavior.Restrict);

            // Odontólogo 1:N Atención
            builder.Entity<AtencionOdontologica>()
                .HasOne(a => a.Odontologo)
                .WithMany()
                .HasForeignKey(a => a.OdontologoId)
                .OnDelete(DeleteBehavior.Restrict);

            // Cita 1:0..1 Atención
            builder.Entity<AtencionOdontologica>()
                .HasOne(a => a.Cita)
                .WithOne()
                .HasForeignKey<AtencionOdontologica>(a => a.CitaId)
                .OnDelete(DeleteBehavior.Restrict);

            // Atención 1:N Diagnóstico
            builder.Entity<Diagnostico>()
                .HasOne(d => d.AtencionOdontologica)
                .WithMany(a => a.Diagnosticos)
                .HasForeignKey(d => d.AtencionOdontologicaId)
                .OnDelete(DeleteBehavior.Cascade);

            // Atención 1:N Evolución
            builder.Entity<EvolucionClinica>()
                .HasOne(e => e.AtencionOdontologica)
                .WithMany(a => a.EvolucionesClinicas)
                .HasForeignKey(e => e.AtencionOdontologicaId)
                .OnDelete(DeleteBehavior.Cascade);

            // Atención 1:N Tratamiento
            builder.Entity<Tratamiento>()
                .HasOne(t => t.AtencionOdontologica)
                .WithMany(a => a.Tratamientos)
                .HasForeignKey(t => t.AtencionOdontologicaId)
                .OnDelete(DeleteBehavior.Cascade);

            // Servicio 1:N Tratamiento
            builder.Entity<Tratamiento>()
                .HasOne(t => t.ServicioOdontologico)
                .WithMany()
                .HasForeignKey(t => t.ServicioOdontologicoId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}

