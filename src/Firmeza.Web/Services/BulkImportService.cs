using System.Globalization;
using System.Text;
using System.ComponentModel.DataAnnotations;
using Firmeza.Web.Data;
using Firmeza.Web.Models;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;

namespace Firmeza.Web.Services;

public sealed record ImportIssue(string Worksheet, int Row, string Message);

public sealed class BulkImportResult
{
    public int ProductsCreated { get; set; }
    public int ProductsUpdated { get; set; }
    public int CustomersCreated { get; set; }
    public int CustomersUpdated { get; set; }
    public int SalesCreated { get; set; }
    public List<ImportIssue> Issues { get; } = [];
    public int SuccessfulRows => ProductsCreated + ProductsUpdated + CustomersCreated + CustomersUpdated + SalesCreated;
}

public sealed class BulkImportService(ApplicationDbContext db, IConfiguration configuration, PdfDocumentService pdfDocuments)
{
    private static readonly string[] ProductSkuHeaders = ["sku", "codigo", "codigoproducto", "referencia", "productsku"];
    private static readonly string[] ProductNameHeaders = ["producto", "nombreproducto", "nombrearticulo", "productonombre", "product", "productname", "articulo", "item"];
    private static readonly string[] ProductCategoryHeaders = ["categoria", "category", "linea"];
    private static readonly string[] PriceHeaders = ["precio", "preciounitario", "precio de venta", "unitprice", "price"];
    private static readonly string[] StockHeaders = ["stock", "existencias", "inventario", "cantidadexistente"];
    private static readonly string[] CustomerDocumentHeaders = ["documento", "numerodocumento", "cedula", "identificacion", "ruc", "customerdocument"];
    private static readonly string[] CustomerFirstNameHeaders = ["nombrecliente", "clientenombre", "firstname", "nombres", "customername", "cliente", "customer"];
    private static readonly string[] CustomerLastNameHeaders = ["apellido", "apellidos", "lastname"];
    private static readonly string[] EmailHeaders = ["correo", "email", "correoelectronico"];
    private static readonly string[] PhoneHeaders = ["telefono", "celular", "phone"];
    private static readonly string[] SaleNumberHeaders = ["venta", "numeroventa", "numerodeventa", "numeroorden", "ordenventa", "factura", "orden", "referenciaventa", "salenumber", "saleid"];
    private static readonly string[] SaleDateHeaders = ["fechaventa", "fecha", "saledate", "createdat"];
    private static readonly string[] QuantityHeaders = ["cantidad", "cantidadvendida", "quantity", "qty"];
    private static readonly string[] SaleStatusHeaders = ["estadoventa", "estado", "status"];
    private static readonly string[] UnitHeaders = ["unidad", "unidadmedida", "unit", "unitofmeasure"];
    private static readonly HashSet<string> SupportedHeaders = ProductSkuHeaders.Concat(ProductNameHeaders)
        .Concat(ProductCategoryHeaders).Concat(PriceHeaders).Concat(StockHeaders).Concat(CustomerDocumentHeaders)
        .Concat(CustomerFirstNameHeaders).Concat(CustomerLastNameHeaders).Concat(EmailHeaders).Concat(PhoneHeaders)
        .Concat(SaleNumberHeaders).Concat(SaleDateHeaders).Concat(QuantityHeaders).Concat(SaleStatusHeaders)
        .Concat(UnitHeaders).Append("nombre").Select(NormalizeHeader).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<BulkImportResult> ImportAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var result = new BulkImportResult();
        var importRows = new List<ImportRow>();
        using (var package = new ExcelPackage())
        {
            await package.LoadAsync(stream, cancellationToken);
            foreach (var sheet in package.Workbook.Worksheets)
            {
                if (sheet.Dimension is null) continue;
                var headers = BuildHeaderMap(sheet);
                if (headers.Count == 0)
                {
                    result.Issues.Add(new ImportIssue(sheet.Name, 1, "No se encontraron encabezados en la primera fila."));
                    continue;
                }
                if (!headers.Keys.Any(SupportedHeaders.Contains))
                {
                    result.Issues.Add(new ImportIssue(sheet.Name, 1, "Los encabezados no coinciden con ningún campo reconocido de productos, clientes o ventas."));
                    continue;
                }

                for (var rowNumber = 2; rowNumber <= sheet.Dimension.End.Row; rowNumber++)
                {
                    if (Enumerable.Range(1, sheet.Dimension.End.Column).All(col => string.IsNullOrWhiteSpace(sheet.Cells[rowNumber, col].Text))) continue;
                    try
                    {
                        importRows.Add(ParseRow(sheet, headers, rowNumber));
                    }
                    catch (FormatException ex)
                    {
                        result.Issues.Add(new ImportIssue(sheet.Name, rowNumber, ex.Message));
                    }
                }
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var generatedReceipts = new List<string>();
        try
        {
            var skuCache = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
            var documentCache = new Dictionary<string, Customer>(StringComparer.OrdinalIgnoreCase);
            var emailOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in importRows)
            {
                if (row.ProductSignal)
                {
                    if (string.IsNullOrWhiteSpace(row.Sku) || string.IsNullOrWhiteSpace(row.ProductName) || row.Price is null or <= 0)
                    {
                        result.Issues.Add(new ImportIssue(row.Sheet, row.RowNumber, "Producto incompleto: se requieren SKU, nombre y precio mayor que cero."));
                        row.Product = null;
                    }
                    else
                    {
                        var key = row.Sku.Trim();
                        if (!skuCache.TryGetValue(key, out var product))
                        {
                            product = await db.Products.FirstOrDefaultAsync(p => p.Sku == key, cancellationToken);
                            if (product is null)
                            {
                                product = new Product { Sku = key, Name = row.ProductName.Trim(), Category = row.Category ?? "Importados", UnitOfMeasure = row.Unit ?? "unidad", UnitPrice = row.Price.Value, Stock = row.Stock ?? 0 };
                                db.Products.Add(product);
                                result.ProductsCreated++;
                            }
                            else
                            {
                                product.Name = row.ProductName.Trim();
                                product.Category = row.Category ?? product.Category;
                                product.UnitOfMeasure = row.Unit ?? product.UnitOfMeasure;
                                product.UnitPrice = row.Price.Value;
                                if (row.Stock.HasValue) product.Stock = row.Stock.Value;
                                product.IsActive = true;
                                result.ProductsUpdated++;
                            }
                            skuCache[key] = product;
                        }
                        else
                        {
                            product.Name = row.ProductName.Trim();
                            product.Category = row.Category ?? product.Category;
                            product.UnitOfMeasure = row.Unit ?? product.UnitOfMeasure;
                            product.UnitPrice = row.Price.Value;
                            if (row.Stock.HasValue) product.Stock = row.Stock.Value;
                        }
                        row.Product = product;
                    }
                }
                else if (row.SaleSignal && !string.IsNullOrWhiteSpace(row.Sku))
                {
                    var key = row.Sku.Trim();
                    if (!skuCache.TryGetValue(key, out var product))
                    {
                        product = await db.Products.FirstOrDefaultAsync(p => p.Sku == key && p.IsActive, cancellationToken);
                        if (product is not null) skuCache[key] = product;
                    }
                    row.Product = product;
                }

                if (row.CustomerSignal)
                {
                    if (string.IsNullOrWhiteSpace(row.DocumentNumber))
                    {
                        result.Issues.Add(new ImportIssue(row.Sheet, row.RowNumber, "Cliente incompleto: se requiere el número de documento."));
                        row.Customer = null;
                    }
                    else
                    {
                        var key = row.DocumentNumber.Trim();
                        if (!string.IsNullOrWhiteSpace(row.Email))
                        {
                            var email = row.Email.Trim();
                            if (!new EmailAddressAttribute().IsValid(email))
                            {
                                result.Issues.Add(new ImportIssue(row.Sheet, row.RowNumber, "El correo del cliente no tiene un formato válido; se importará el resto de sus datos."));
                                row.Email = null;
                            }
                            else if ((emailOwners.TryGetValue(email, out var owner) && !string.Equals(owner, key, StringComparison.OrdinalIgnoreCase)) ||
                                await db.Customers.AsNoTracking().AnyAsync(c => c.Email == email && c.DocumentNumber != key, cancellationToken))
                            {
                                result.Issues.Add(new ImportIssue(row.Sheet, row.RowNumber, "El correo ya está asignado a otro cliente; se importará el resto de sus datos."));
                                row.Email = null;
                            }
                            else
                            {
                                emailOwners[email] = key;
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(row.Phone) && !new PhoneAttribute().IsValid(row.Phone))
                        {
                            result.Issues.Add(new ImportIssue(row.Sheet, row.RowNumber, "El teléfono no tiene un formato válido; se importará el resto de los datos."));
                            row.Phone = null;
                        }
                        if (!documentCache.TryGetValue(key, out var customer))
                        {
                            customer = await db.Customers.FirstOrDefaultAsync(c => c.DocumentNumber == key, cancellationToken);
                            if (customer is null && string.IsNullOrWhiteSpace(row.CustomerFirstName))
                            {
                                result.Issues.Add(new ImportIssue(row.Sheet, row.RowNumber, "No se encontró el cliente por documento y no se proporcionó un nombre para crearlo."));
                                row.Customer = null;
                                continue;
                            }
                            if (customer is null)
                            {
                                customer = new Customer { DocumentNumber = key, FirstName = row.CustomerFirstName!.Trim(), LastName = row.CustomerLastName?.Trim() ?? string.Empty, DocumentType = row.DocumentType ?? "Documento", Email = row.Email?.Trim(), Phone = row.Phone?.Trim() };
                                db.Customers.Add(customer);
                                result.CustomersCreated++;
                            }
                            else
                            {
                                if (!string.IsNullOrWhiteSpace(row.CustomerFirstName)) customer.FirstName = row.CustomerFirstName.Trim();
                                if (!string.IsNullOrWhiteSpace(row.CustomerLastName)) customer.LastName = row.CustomerLastName.Trim();
                                customer.DocumentType = row.DocumentType ?? customer.DocumentType;
                                customer.Email = row.Email?.Trim() ?? customer.Email;
                                customer.Phone = row.Phone?.Trim() ?? customer.Phone;
                                result.CustomersUpdated++;
                            }
                            documentCache[key] = customer;
                        }
                        else
                        {
                            if (!string.IsNullOrWhiteSpace(row.CustomerFirstName)) customer.FirstName = row.CustomerFirstName.Trim();
                            if (!string.IsNullOrWhiteSpace(row.CustomerLastName)) customer.LastName = row.CustomerLastName.Trim();
                            customer.Email = row.Email?.Trim() ?? customer.Email;
                            customer.Phone = row.Phone?.Trim() ?? customer.Phone;
                        }
                        row.Customer = customer;
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            var saleRows = importRows.Where(row => row.SaleSignal).ToList();
            var createdSaleReferences = new List<string>();
            foreach (var group in saleRows.GroupBy(row => row.SaleNumber!, StringComparer.OrdinalIgnoreCase))
            {
                var rows = group.ToList();
                var first = rows[0];
                if (rows.Any(row => row.Customer is null || row.Product is null || row.Quantity is null or <= 0))
                {
                    foreach (var row in rows.Where(row => row.Customer is null || row.Product is null || row.Quantity is null or <= 0))
                        result.Issues.Add(new ImportIssue(row.Sheet, row.RowNumber, "Detalle de venta inválido: la fila debe tener cliente, producto y cantidad entera mayor que cero."));
                    continue;
                }
                var customerIds = rows.Select(row => row.Customer!.Id).Distinct().ToList();
                if (customerIds.Count != 1)
                {
                    result.Issues.Add(new ImportIssue(first.Sheet, first.RowNumber, $"La venta {group.Key} contiene más de un cliente; se omitió."));
                    continue;
                }
                var existingSale = await db.Sales.Include(s => s.Details).FirstOrDefaultAsync(s => s.ExternalReference == group.Key, cancellationToken);
                if (existingSale is not null)
                {
                    result.Issues.Add(new ImportIssue(first.Sheet, first.RowNumber, $"La venta {group.Key} ya existe y fue omitida para evitar duplicados."));
                    continue;
                }
                var details = rows.Select(row =>
                {
                    var unitPrice = row.SalePrice ?? row.Product!.UnitPrice;
                    return new SaleDetail { ProductId = row.Product!.Id, Quantity = row.Quantity!.Value, UnitPrice = unitPrice, LineTotal = unitPrice * row.Quantity.Value };
                }).ToList();
                var sale = new Sale
                {
                    CustomerId = customerIds[0],
                    CreatedAtUtc = first.SaleDate ?? DateTime.UtcNow,
                    Status = first.SaleStatus ?? "Completada",
                    ExternalReference = group.Key,
                    Details = details,
                    Subtotal = details.Sum(detail => detail.LineTotal),
                    TaxRate = configuration.GetValue<decimal?>("TaxRate") ?? 0.15m,
                    TaxAmount = Math.Round(details.Sum(detail => detail.LineTotal) * (configuration.GetValue<decimal?>("TaxRate") ?? 0.15m), 2),
                    Total = Math.Round(details.Sum(detail => detail.LineTotal) * (1 + (configuration.GetValue<decimal?>("TaxRate") ?? 0.15m)), 2)
                };
                db.Sales.Add(sale);
                createdSaleReferences.Add(group.Key);
                result.SalesCreated++;
            }

            await db.SaveChangesAsync(cancellationToken);
            var importedSales = await db.Sales.AsNoTracking().Where(s => createdSaleReferences.Contains(s.ExternalReference!))
                .Include(s => s.Customer).Include(s => s.Details).ThenInclude(detail => detail.Product).ToListAsync(cancellationToken);
            foreach (var sale in importedSales)
                generatedReceipts.Add(await pdfDocuments.SaveReceiptAsync(sale, cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            foreach (var receipt in generatedReceipts)
                if (File.Exists(receipt)) File.Delete(receipt);
            throw;
        }
    }

    private static Dictionary<string, int> BuildHeaderMap(ExcelWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var col = 1; col <= sheet.Dimension!.End.Column; col++)
        {
            var header = NormalizeHeader(sheet.Cells[1, col].Text);
            if (!string.IsNullOrEmpty(header) && !map.ContainsKey(header)) map.Add(header, col);
        }
        return map;
    }

    private static ImportRow ParseRow(ExcelWorksheet sheet, Dictionary<string, int> headers, int rowNumber)
    {
        string? Read(params string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                var normalized = NormalizeHeader(candidate);
                if (headers.TryGetValue(normalized, out var column))
                {
                    var value = sheet.Cells[rowNumber, column].Text.Trim();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
            return null;
        }

        decimal? DecimalValue(string? value, string label)
        {
            if (value is null) return null;
            var normalized = new string(value.Where(character => char.IsDigit(character) || character is '.' or ',' or '-' or '+').ToArray());
            var lastComma = normalized.LastIndexOf(',');
            var lastDot = normalized.LastIndexOf('.');
            if (lastComma >= 0 && lastDot >= 0)
            {
                normalized = lastComma > lastDot
                    ? normalized.Replace(".", string.Empty).Replace(',', '.')
                    : normalized.Replace(",", string.Empty);
            }
            else if (lastComma >= 0)
            {
                var decimalPlaces = normalized.Length - lastComma - 1;
                normalized = decimalPlaces is > 0 and <= 2
                    ? normalized.Replace(',', '.')
                    : normalized.Replace(",", string.Empty);
            }
            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            throw new FormatException($"El valor de {label} '{value}' no es un número válido.");
        }

        int? IntegerValue(string? value, string label)
        {
            if (value is null) return null;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            throw new FormatException($"El valor de {label} '{value}' debe ser un número entero.");
        }

        DateTime? DateValue(string? value)
        {
            if (value is null) return null;
            if (DateTime.TryParse(value, CultureInfo.GetCultureInfo("es"), DateTimeStyles.AssumeUniversal, out var parsed)) return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed)) return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            throw new FormatException($"La fecha de venta '{value}' no es válida.");
        }

        void RequireLength(string? value, int maximum, string label)
        {
            if (value?.Length > maximum) throw new FormatException($"El campo {label} supera el máximo de {maximum} caracteres.");
        }

        var sku = Read(ProductSkuHeaders);
        var productName = Read(ProductNameHeaders);
        var saleNumber = Read(SaleNumberHeaders);
        var customerDocument = Read(CustomerDocumentHeaders);
        var customerName = Read(CustomerFirstNameHeaders);
        var customerLastName = Read(CustomerLastNameHeaders);
        var documentType = Read("tipodocumento", "documenttype") ?? (Read("ruc") is not null ? "RUC" : null);
        var email = Read(EmailHeaders);
        var phone = Read(PhoneHeaders);
        var saleDate = DateValue(Read(SaleDateHeaders));
        if (saleDate.HasValue && string.IsNullOrWhiteSpace(saleNumber))
            throw new FormatException("La fila tiene fecha de venta, pero no contiene un número de venta para agrupar sus detalles.");
        var qty = IntegerValue(Read(QuantityHeaders), "cantidad");
        var unitPrice = DecimalValue(Read(PriceHeaders), "precio");
        var stock = IntegerValue(Read(StockHeaders), "stock");
        if (stock is < 0) throw new FormatException("El stock no puede ser negativo.");
        if (unitPrice is < 0) throw new FormatException("El precio no puede ser negativo.");
        var category = Read(ProductCategoryHeaders);
        var unit = Read(UnitHeaders);
        var genericName = Read("nombre");
        productName ??= category is not null || (sku is not null && customerDocument is null) ? genericName : null;
        customerName ??= customerDocument is not null ? genericName : null;
        RequireLength(sku, 32, "SKU");
        RequireLength(productName, 120, "nombre de producto");
        RequireLength(category, 80, "categoría");
        RequireLength(unit, 24, "unidad de medida");
        RequireLength(customerDocument, 24, "documento de cliente");
        RequireLength(customerName, 80, "nombre de cliente");
        RequireLength(customerLastName, 80, "apellido de cliente");
        RequireLength(documentType, 32, "tipo de documento");
        RequireLength(email, 254, "correo electrónico");
        RequireLength(phone, 24, "teléfono");
        RequireLength(saleNumber, 40, "número de venta");
        RequireLength(Read(SaleStatusHeaders), 24, "estado de venta");
        var productSignal = productName is not null || category is not null || stock is not null || (sku is not null && saleNumber is null);
        var customerSignal = customerDocument is not null || customerName is not null;
        var saleSignal = saleNumber is not null;
        return new ImportRow
        {
            Sheet = sheet.Name, RowNumber = rowNumber,
            Sku = sku, ProductName = productName,
            Category = category, Unit = unit, Price = unitPrice, Stock = stock,
            ProductSignal = productSignal, CustomerSignal = customerSignal,
            DocumentNumber = customerDocument,
            DocumentType = documentType,
            CustomerFirstName = customerName, CustomerLastName = customerLastName,
            Email = email, Phone = phone,
            SaleSignal = saleSignal, SaleNumber = saleNumber, SaleDate = saleDate,
            Quantity = qty, SalePrice = saleSignal ? unitPrice : null, SaleStatus = Read(SaleStatusHeaders)
        };
    }

    public static string NormalizeHeader(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character)) builder.Append(character);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed class ImportRow
    {
        public string Sheet { get; set; } = string.Empty;
        public int RowNumber { get; set; }
        public string? Sku { get; set; }
        public string? ProductName { get; set; }
        public string? Category { get; set; }
        public string? Unit { get; set; }
        public decimal? Price { get; set; }
        public int? Stock { get; set; }
        public bool ProductSignal { get; set; }
        public bool CustomerSignal { get; set; }
        public string? DocumentNumber { get; set; }
        public string? DocumentType { get; set; }
        public string? CustomerFirstName { get; set; }
        public string? CustomerLastName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public bool SaleSignal { get; set; }
        public string? SaleNumber { get; set; }
        public DateTime? SaleDate { get; set; }
        public int? Quantity { get; set; }
        public decimal? SalePrice { get; set; }
        public string? SaleStatus { get; set; }
        public Product? Product { get; set; }
        public Customer? Customer { get; set; }
    }
}
