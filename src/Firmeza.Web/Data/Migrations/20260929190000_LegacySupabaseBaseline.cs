using Firmeza.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Firmeza.Web.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260929190000_LegacySupabaseBaseline")]
public sealed class LegacySupabaseBaseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $migration$
            BEGIN
                IF to_regclass('public."Customers"') IS NULL
                   AND to_regclass('public."Products"') IS NULL
                   AND to_regclass('public."Sales"') IS NULL
                   AND to_regclass('public."SaleDetails"') IS NULL THEN
                    RETURN;
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM public."__EFMigrationsHistory"
                    WHERE "MigrationId" = '20260929191805_InitialCreate'
                ) THEN
                    RETURN;
                END IF;

                IF to_regclass('public."Customers"') IS NULL
                   OR to_regclass('public."Products"') IS NULL
                   OR to_regclass('public."Sales"') IS NULL
                   OR to_regclass('public."SaleDetails"') IS NULL THEN
                    RAISE EXCEPTION 'Legacy Firmeza schema is incomplete; no changes were applied.';
                END IF;

                IF to_regclass('public."AspNetRoles"') IS NOT NULL
                   OR to_regclass('public."AspNetUsers"') IS NOT NULL
                   OR to_regclass('public."AspNetRoleClaims"') IS NOT NULL
                   OR to_regclass('public."AspNetUserClaims"') IS NOT NULL
                   OR to_regclass('public."AspNetUserLogins"') IS NOT NULL
                   OR to_regclass('public."AspNetUserRoles"') IS NOT NULL
                   OR to_regclass('public."AspNetUserTokens"') IS NOT NULL THEN
                    RAISE EXCEPTION 'Identity tables already exist without the initial EF migration record; no changes were applied.';
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = 'Customers' AND column_name = 'Name'
                ) OR NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = 'Products' AND column_name = 'Price'
                ) OR NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = 'Sales' AND column_name = 'Date'
                ) OR NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'public' AND table_name = 'SaleDetails' AND column_name = 'UnitPrice'
                ) THEN
                    RAISE EXCEPTION 'The existing schema does not match the inspected legacy Firmeza schema; no changes were applied.';
                END IF;

                IF EXISTS (
                    SELECT 1 FROM public."Products"
                    WHERE length("Name") > 120
                       OR "Price" <= 0
                       OR "Price" >= 10000000000
                       OR "Price" <> round("Price", 2)
                       OR "Stock" < 0
                ) THEN
                    RAISE EXCEPTION 'A legacy product cannot fit the current Firmeza validation rules; no changes were applied.';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM public."Customers"
                    WHERE length(CASE
                            WHEN btrim("Name") ~ '[[:space:]]'
                                THEN regexp_replace(btrim("Name"), '[[:space:]]+[^[:space:]]+$', '')
                            ELSE btrim("Name")
                        END) > 80
                       OR length(CASE
                            WHEN btrim("Name") ~ '[[:space:]]'
                                THEN regexp_replace(btrim("Name"), '^.*[[:space:]]+', '')
                            ELSE 'Pendiente'
                        END) > 80
                       OR length("Email") > 254
                       OR length("Phone") > 24
                       OR btrim("Name") = ''
                ) THEN
                    RAISE EXCEPTION 'A legacy customer cannot be mapped safely to the current Firmeza model; no changes were applied.';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM public."Customers"
                    WHERE "Email" IS NOT NULL
                    GROUP BY "Email"
                    HAVING count(*) > 1
                ) THEN
                    RAISE EXCEPTION 'Legacy customers contain duplicate emails; the unique index cannot be created safely.';
                END IF;

                IF EXISTS (
                    SELECT 1 FROM public."Sales"
                    WHERE "Total" < 0
                       OR "Total" >= 1000000000000
                       OR "Total" <> round("Total", 2)
                ) OR EXISTS (
                    SELECT 1 FROM public."SaleDetails"
                    WHERE "UnitPrice" < 0
                       OR "UnitPrice" >= 10000000000
                       OR "UnitPrice" <> round("UnitPrice", 2)
                       OR "Quantity" < 0
                       OR "Quantity" * "UnitPrice" >= 1000000000000
                       OR "Quantity" * "UnitPrice" <> round("Quantity" * "UnitPrice", 2)
                ) THEN
                    RAISE EXCEPTION 'Legacy sale amounts cannot be represented at the current precision; no changes were applied.';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM public."Sales" s
                    LEFT JOIN public."Customers" c ON c."Id" = s."CustomerId"
                    WHERE c."Id" IS NULL
                ) OR EXISTS (
                    SELECT 1
                    FROM public."SaleDetails" d
                    LEFT JOIN public."Sales" s ON s."Id" = d."SaleId"
                    WHERE s."Id" IS NULL
                ) OR EXISTS (
                    SELECT 1
                    FROM public."SaleDetails" d
                    LEFT JOIN public."Products" p ON p."Id" = d."ProductId"
                    WHERE p."Id" IS NULL
                ) THEN
                    RAISE EXCEPTION 'Legacy sales contain orphaned references; no changes were applied.';
                END IF;

                ALTER TABLE public."Customers" RENAME COLUMN "Name" TO "FirstName";
                ALTER TABLE public."Customers"
                    ALTER COLUMN "FirstName" TYPE character varying(80),
                    ALTER COLUMN "Email" TYPE character varying(254),
                    ALTER COLUMN "Email" DROP NOT NULL,
                    ALTER COLUMN "Phone" TYPE character varying(24),
                    ALTER COLUMN "Phone" DROP NOT NULL;
                ALTER TABLE public."Customers"
                    ADD COLUMN "LastName" character varying(80),
                    ADD COLUMN "DocumentNumber" character varying(24),
                    ADD COLUMN "DocumentType" character varying(32) NOT NULL DEFAULT 'No especificado',
                    ADD COLUMN "Address" character varying(240),
                    ADD COLUMN "Age" integer,
                    ADD COLUMN "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now();

                UPDATE public."Customers"
                SET "FirstName" = CASE
                        WHEN btrim("FirstName") ~ '[[:space:]]'
                            THEN regexp_replace(btrim("FirstName"), '[[:space:]]+[^[:space:]]+$', '')
                        ELSE btrim("FirstName")
                    END,
                    "LastName" = CASE
                        WHEN btrim("FirstName") ~ '[[:space:]]'
                            THEN regexp_replace(btrim("FirstName"), '^.*[[:space:]]+', '')
                        ELSE 'Pendiente'
                    END,
                    "DocumentNumber" = 'LEGACY-CUST-' || "Id"::text;

                ALTER TABLE public."Customers"
                    ALTER COLUMN "LastName" SET NOT NULL,
                    ALTER COLUMN "DocumentNumber" SET NOT NULL,
                    ALTER COLUMN "DocumentType" DROP DEFAULT,
                    ALTER COLUMN "CreatedAtUtc" DROP DEFAULT;

                ALTER TABLE public."Products" RENAME COLUMN "Price" TO "UnitPrice";
                ALTER TABLE public."Products"
                    ALTER COLUMN "Name" TYPE character varying(120),
                    ALTER COLUMN "UnitPrice" TYPE numeric(12,2);
                ALTER TABLE public."Products"
                    ADD COLUMN "Sku" character varying(32),
                    ADD COLUMN "Description" character varying(800),
                    ADD COLUMN "Category" character varying(80) NOT NULL DEFAULT 'Sin categoría',
                    ADD COLUMN "UnitOfMeasure" character varying(24) NOT NULL DEFAULT 'unidad',
                    ADD COLUMN "MinimumStock" integer NOT NULL DEFAULT 0,
                    ADD COLUMN "IsActive" boolean NOT NULL DEFAULT true,
                    ADD COLUMN "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now();

                UPDATE public."Products"
                SET "Sku" = 'LEGACY-PROD-' || "Id"::text;

                ALTER TABLE public."Products"
                    ALTER COLUMN "Sku" SET NOT NULL,
                    ALTER COLUMN "Category" DROP DEFAULT,
                    ALTER COLUMN "UnitOfMeasure" DROP DEFAULT,
                    ALTER COLUMN "MinimumStock" DROP DEFAULT,
                    ALTER COLUMN "IsActive" DROP DEFAULT,
                    ALTER COLUMN "CreatedAtUtc" DROP DEFAULT;

                ALTER TABLE public."Sales" RENAME COLUMN "Date" TO "CreatedAtUtc";
                ALTER TABLE public."Sales"
                    ALTER COLUMN "Total" TYPE numeric(14,2);
                ALTER TABLE public."Sales"
                    ADD COLUMN "Status" character varying(24) NOT NULL DEFAULT 'Registrada';
                ALTER TABLE public."Sales"
                    ALTER COLUMN "Status" DROP DEFAULT;

                ALTER TABLE public."SaleDetails"
                    ALTER COLUMN "UnitPrice" TYPE numeric(12,2);
                ALTER TABLE public."SaleDetails"
                    ADD COLUMN "LineTotal" numeric(14,2) NOT NULL DEFAULT 0;

                UPDATE public."SaleDetails"
                SET "LineTotal" = "Quantity" * "UnitPrice";

                ALTER TABLE public."SaleDetails"
                    ALTER COLUMN "LineTotal" DROP DEFAULT;

                CREATE TABLE public."AspNetRoles" (
                    "Id" text NOT NULL,
                    "Name" character varying(256),
                    "NormalizedName" character varying(256),
                    "ConcurrencyStamp" text,
                    CONSTRAINT "PK_AspNetRoles" PRIMARY KEY ("Id")
                );

                CREATE TABLE public."AspNetUsers" (
                    "Id" text NOT NULL,
                    "FullName" text,
                    "CustomerId" integer,
                    "UserName" character varying(256),
                    "NormalizedUserName" character varying(256),
                    "Email" character varying(256),
                    "NormalizedEmail" character varying(256),
                    "EmailConfirmed" boolean NOT NULL,
                    "PasswordHash" text,
                    "SecurityStamp" text,
                    "ConcurrencyStamp" text,
                    "PhoneNumber" text,
                    "PhoneNumberConfirmed" boolean NOT NULL,
                    "TwoFactorEnabled" boolean NOT NULL,
                    "LockoutEnd" timestamp with time zone,
                    "LockoutEnabled" boolean NOT NULL,
                    "AccessFailedCount" integer NOT NULL,
                    CONSTRAINT "PK_AspNetUsers" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_AspNetUsers_Customers_CustomerId"
                        FOREIGN KEY ("CustomerId") REFERENCES public."Customers" ("Id") ON DELETE SET NULL
                );

                CREATE TABLE public."AspNetRoleClaims" (
                    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                    "RoleId" text NOT NULL,
                    "ClaimType" text,
                    "ClaimValue" text,
                    CONSTRAINT "PK_AspNetRoleClaims" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_AspNetRoleClaims_AspNetRoles_RoleId"
                        FOREIGN KEY ("RoleId") REFERENCES public."AspNetRoles" ("Id") ON DELETE CASCADE
                );

                CREATE TABLE public."AspNetUserClaims" (
                    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                    "UserId" text NOT NULL,
                    "ClaimType" text,
                    "ClaimValue" text,
                    CONSTRAINT "PK_AspNetUserClaims" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_AspNetUserClaims_AspNetUsers_UserId"
                        FOREIGN KEY ("UserId") REFERENCES public."AspNetUsers" ("Id") ON DELETE CASCADE
                );

                CREATE TABLE public."AspNetUserLogins" (
                    "LoginProvider" text NOT NULL,
                    "ProviderKey" text NOT NULL,
                    "ProviderDisplayName" text,
                    "UserId" text NOT NULL,
                    CONSTRAINT "PK_AspNetUserLogins" PRIMARY KEY ("LoginProvider", "ProviderKey"),
                    CONSTRAINT "FK_AspNetUserLogins_AspNetUsers_UserId"
                        FOREIGN KEY ("UserId") REFERENCES public."AspNetUsers" ("Id") ON DELETE CASCADE
                );

                CREATE TABLE public."AspNetUserRoles" (
                    "UserId" text NOT NULL,
                    "RoleId" text NOT NULL,
                    CONSTRAINT "PK_AspNetUserRoles" PRIMARY KEY ("UserId", "RoleId"),
                    CONSTRAINT "FK_AspNetUserRoles_AspNetRoles_RoleId"
                        FOREIGN KEY ("RoleId") REFERENCES public."AspNetRoles" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_AspNetUserRoles_AspNetUsers_UserId"
                        FOREIGN KEY ("UserId") REFERENCES public."AspNetUsers" ("Id") ON DELETE CASCADE
                );

                CREATE TABLE public."AspNetUserTokens" (
                    "UserId" text NOT NULL,
                    "LoginProvider" text NOT NULL,
                    "Name" text NOT NULL,
                    "Value" text,
                    CONSTRAINT "PK_AspNetUserTokens" PRIMARY KEY ("UserId", "LoginProvider", "Name"),
                    CONSTRAINT "FK_AspNetUserTokens_AspNetUsers_UserId"
                        FOREIGN KEY ("UserId") REFERENCES public."AspNetUsers" ("Id") ON DELETE CASCADE
                );

                ALTER TABLE public."Sales"
                    ADD CONSTRAINT "FK_Sales_Customers_CustomerId"
                    FOREIGN KEY ("CustomerId") REFERENCES public."Customers" ("Id") ON DELETE RESTRICT;
                ALTER TABLE public."SaleDetails"
                    ADD CONSTRAINT "FK_SaleDetails_Products_ProductId"
                    FOREIGN KEY ("ProductId") REFERENCES public."Products" ("Id") ON DELETE RESTRICT,
                    ADD CONSTRAINT "FK_SaleDetails_Sales_SaleId"
                    FOREIGN KEY ("SaleId") REFERENCES public."Sales" ("Id") ON DELETE CASCADE;

                CREATE UNIQUE INDEX "IX_Customers_DocumentNumber" ON public."Customers" ("DocumentNumber");
                CREATE UNIQUE INDEX "IX_Customers_Email" ON public."Customers" ("Email");
                CREATE UNIQUE INDEX "IX_Products_Sku" ON public."Products" ("Sku");
                CREATE INDEX "IX_Sales_CustomerId" ON public."Sales" ("CustomerId");
                CREATE INDEX "IX_SaleDetails_ProductId" ON public."SaleDetails" ("ProductId");
                CREATE INDEX "IX_SaleDetails_SaleId" ON public."SaleDetails" ("SaleId");
                CREATE INDEX "IX_AspNetRoleClaims_RoleId" ON public."AspNetRoleClaims" ("RoleId");
                CREATE UNIQUE INDEX "RoleNameIndex" ON public."AspNetRoles" ("NormalizedName");
                CREATE INDEX "IX_AspNetUserClaims_UserId" ON public."AspNetUserClaims" ("UserId");
                CREATE INDEX "IX_AspNetUserLogins_UserId" ON public."AspNetUserLogins" ("UserId");
                CREATE INDEX "IX_AspNetUserRoles_RoleId" ON public."AspNetUserRoles" ("RoleId");
                CREATE INDEX "EmailIndex" ON public."AspNetUsers" ("NormalizedEmail");
                CREATE INDEX "IX_AspNetUsers_CustomerId" ON public."AspNetUsers" ("CustomerId");
                CREATE UNIQUE INDEX "UserNameIndex" ON public."AspNetUsers" ("NormalizedUserName");

                INSERT INTO public."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260929191805_InitialCreate', '8.0.11')
                ON CONFLICT ("MigrationId") DO NOTHING;
            END
            $migration$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(
            "The legacy Supabase baseline is data-preserving and cannot be reverted automatically. Restore the pre-migration backup instead.");
    }
}
