﻿// <auto-generated />
using AudiobookManager.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace AudiobookManager.Database.Migrations
{
    [DbContext(typeof(DatabaseContext))]
    [Migration("20221110071342_InitialMigration")]
    partial class InitialMigration
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "6.0.11");

            modelBuilder.Entity("AudiobookManager.Database.Models.SeriesMappingDb", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER")
                        .HasColumnName("id");

                    b.Property<string>("MappedSeries")
                        .IsRequired()
                        .HasColumnType("TEXT")
                        .HasColumnName("mapped_series");

                    b.Property<string>("Regex")
                        .IsRequired()
                        .HasColumnType("TEXT")
                        .HasColumnName("regex");

                    b.Property<bool>("WarnAboutPart")
                        .HasColumnType("INTEGER")
                        .HasColumnName("warn_about_part");

                    b.HasKey("Id");

                    b.HasIndex("Regex")
                        .IsUnique();

                    b.ToTable("series_mapping");
                });
#pragma warning restore 612, 618
        }
    }
}
