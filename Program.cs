using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace InventoryManagementSystem
{
    public class InventoryItem
    {
        public string RecordId { get; set; }
        public string BatchName { get; set; }
        public string Sku { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsActive { get; set; }
        public string Checksum { get; set; }

        public string ToCsvLine()
        {
            return $"{RecordId},{EscapeCsv(BatchName)},{EscapeCsv(Sku)},{Quantity},{UnitPrice}," +
                   $"{CreatedAt:o},{UpdatedAt:o},{IsActive},{Checksum}";
        }

        public static InventoryItem FromCsvLine(string line)
        {
            var parts = ParseCsvLine(line);
            if (parts.Count < 9) return null;

            return new InventoryItem
            {
                RecordId = parts[0],
                BatchName = parts[1],
                Sku = parts[2],
                Quantity = int.Parse(parts[3]),
                UnitPrice = decimal.Parse(parts[4]),
                CreatedAt = DateTime.Parse(parts[5]),
                UpdatedAt = DateTime.Parse(parts[6]),
                IsActive = bool.Parse(parts[7]),
                Checksum = parts[8]
            };
        }

        public string CalculateChecksum()
        {
            string rawData = $"{RecordId}|{BatchName}|{Sku}|{Quantity}|{UnitPrice}|{CreatedAt:o}|{UpdatedAt:o}|{IsActive}";
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                return Convert.ToBase64String(bytes);
            }
        }

        private static string EscapeCsv(string field)
        {
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n") || field.Contains("\r"))
            {
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            }
            return field;
        }

        private static List<string> ParseCsvLine(string line)
        {
            List<string> result = new List<string>();
            StringBuilder currentStr = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            currentStr.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        currentStr.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        result.Add(currentStr.ToString());
                        currentStr.Clear();
                    }
                    else
                    {
                        currentStr.Append(c);
                    }
                }
            }
            result.Add(currentStr.ToString());
            return result;
        }
    }

    public class StorageInitializer
    {
        private readonly string _dataDir;
        private readonly string _dataFilePath;
        private readonly string _auditFilePath;

        public StorageInitializer(string dataDir, string dataFilePath, string auditFilePath)
        {
            _dataDir = dataDir;
            _dataFilePath = dataFilePath;
            _auditFilePath = auditFilePath;
        }

        public void Initialize()
        {
            try
            {
                if (!Directory.Exists(_dataDir))
                {
                    Directory.CreateDirectory(_dataDir);
                }
                if (!File.Exists(_dataFilePath))
                {
                    File.WriteAllText(_dataFilePath, "RecordId,BatchName,Sku,Quantity,UnitPrice,CreatedAt,UpdatedAt,IsActive,Checksum" + Environment.NewLine);
                }
                if (!File.Exists(_auditFilePath))
                {
                    File.WriteAllText(_auditFilePath, "Timestamp,Action,Details" + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Storage Initialization Error: {ex.Message}");
                throw;
            }
        }
    }

    public class AuditLogger
    {
        private readonly string _auditFilePath;

        public AuditLogger(string auditFilePath)
        {
            _auditFilePath = auditFilePath;
        }

        public void Log(string action, string details)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string logLine = $"{timestamp},{action},\"{details.Replace("\"", "\"\"")}\"";
                File.AppendAllText(_auditFilePath, logLine + Environment.NewLine);
            }
            catch
            {
            }
        }
    }

    public class ValidationComponent
    {
        public bool ValidateBatchName(string name, out string error)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Batch Name cannot be empty.";
                return false;
            }
            error = null;
            return true;
        }

        public bool ValidateSku(string sku, out string error)
        {
            if (string.IsNullOrWhiteSpace(sku))
            {
                error = "SKU cannot be empty.";
                return false;
            }
            error = null;
            return true;
        }

        public bool ValidateQuantity(string qtyStr, out int quantity, out string error)
        {
            if (!int.TryParse(qtyStr, out quantity) || quantity < 0)
            {
                error = "Quantity must be a non-negative integer.";
                return false;
            }
            error = null;
            return true;
        }

        public bool ValidateUnitPrice(string priceStr, out decimal price, out string error)
        {
            if (!decimal.TryParse(priceStr, out price) || price < 0)
            {
                error = "Unit Price must be a non-negative decimal number.";
                return false;
            }
            error = null;
            return true;
        }
    }

    public class FileRepository
    {
        private readonly string _filePath;
        private readonly AuditLogger _auditLogger;

        public FileRepository(string filePath, AuditLogger auditLogger)
        {
            _filePath = filePath;
            _auditLogger = auditLogger;
        }

        public List<InventoryItem> GetAll()
        {
            var items = new List<InventoryItem>();
            try
            {
                var lines = File.ReadAllLines(_filePath);
                if (lines.Length <= 1) return items;

                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    try
                    {
                        var item = InventoryItem.FromCsvLine(lines[i]);
                        if (item != null)
                        {
                            if (item.Checksum != item.CalculateChecksum())
                            {
                                _auditLogger.Log("Error", $"Data integrity corruption detected for Record ID: {item.RecordId}");
                            }
                            items.Add(item);
                        }
                    }
                    catch
                    {
                        _auditLogger.Log("Error", $"Malformed record detected at line {i + 1}");
                    }
                }
                _auditLogger.Log("Read", $"Retrieved {items.Count} items from storage.");
            }
            catch (IOException ex)
            {
                _auditLogger.Log("Error", $"IO Exception during GetAll: {ex.Message}");
                Console.WriteLine($"File Access Error: {ex.Message}");
            }
            return items;
        }

        public void SaveAll(List<InventoryItem> items)
        {
            try
            {
                var lines = new List<string>
                {
                    "RecordId,BatchName,Sku,Quantity,UnitPrice,CreatedAt,UpdatedAt,IsActive,Checksum"
                };
                foreach (var item in items)
                {
                    lines.Add(item.ToCsvLine());
                }
                File.WriteAllLines(_filePath, lines);
            }
            catch (IOException ex)
            {
                _auditLogger.Log("Error", $"IO Exception during SaveAll: {ex.Message}");
                Console.WriteLine($"File Access Error: {ex.Message}");
            }
        }

        public void Add(InventoryItem item)
        {
            var items = GetAll();
            items.Add(item);
            SaveAll(items);
            _auditLogger.Log("Add", $"Added record ID: {item.RecordId}");
        }

        public void Update(InventoryItem updatedItem)
        {
            var items = GetAll();
            int index = items.FindIndex(i => i.RecordId == updatedItem.RecordId);
            if (index != -1)
            {
                items[index] = updatedItem;
                SaveAll(items);
                _auditLogger.Log("Update", $"Updated record ID: {updatedItem.RecordId}");
            }
        }
    }

    public class ReportGenerator
    {
        private readonly FileRepository _repo;

        public ReportGenerator(FileRepository repo)
        {
            _repo = repo;
        }

        public void GenerateValuableInventoryReport()
        {
            var items = _repo.GetAll().Where(i => i.IsActive).ToList();
            decimal totalValue = items.Sum(i => i.Quantity * i.UnitPrice);

            Console.WriteLine("\n============================ REPORT: VALUABLE INVENTORY ============================");
            Console.WriteLine($"{"ID",-8} | {"Batch Name",-20} | {"SKU",-12} | {"Qty",-6} | {"Price",-10} | {"Total Value",-12}");
            Console.WriteLine(new string('-', 76));

            foreach (var item in items)
            {
                decimal value = item.Quantity * item.UnitPrice;
                Console.WriteLine($"{item.RecordId,-8} | {Truncate(item.BatchName, 20),-20} | {Truncate(item.Sku, 12),-12} | {item.Quantity,-6} | {item.UnitPrice,-10:C} | {value,-12:C}");
            }

            Console.WriteLine(new string('-', 76));
            Console.WriteLine($"Total Active Batches: {items.Count}");
            Console.WriteLine($"Total Cumulative Inventory Value: {totalValue:C}");
            Console.WriteLine("====================================================================================");
        }

        private string Truncate(string value, int maxChars)
        {
            return value.Length <= maxChars ? value : value.Substring(0, maxChars - 3) + "...";
        }
    }

    class Program
    {
        private static readonly string DataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        private static readonly string DataFilePath = Path.Combine(DataDirectory, "inventory.csv");
        private static readonly string AuditFilePath = Path.Combine(DataDirectory, "audit.log");

        private static StorageInitializer _storageInit;
        private static AuditLogger _auditLogger;
        private static ValidationComponent _validator;
        private static FileRepository _repository;
        private static ReportGenerator _reportGenerator;

        static void Main(string[] args)
        {
            _storageInit = new StorageInitializer(DataDirectory, DataFilePath, AuditFilePath);
            _auditLogger = new AuditLogger(AuditFilePath);
            _validator = new ValidationComponent();
            _repository = new FileRepository(DataFilePath, _auditLogger);
            _reportGenerator = new ReportGenerator(_repository);

            try
            {
                _storageInit.Initialize();
            }
            catch
            {
                Console.WriteLine("Critical storage failure. Press any key to exit.");
                Console.ReadKey();
                return;
            }

            RunMenuLoop();
        }

        private static void RunMenuLoop()
        {
            bool exit = false;
            while (!exit)
            {
                Console.Clear();
                Console.WriteLine("=== INVENTORY BATCHES MANAGEMENT SYSTEM ===");
                Console.WriteLine("1. Add Record");
                Console.WriteLine("2. View Active Records (with Search/Filter)");
                Console.WriteLine("3. Update Record");
                Console.WriteLine("4. Soft Delete Record (Mark Inactive)");
                Console.WriteLine("5. Hard Delete Record (Permanent)");
                Console.WriteLine("6. Generate Report");
                Console.WriteLine("7. Exit");
                Console.Write("Select an option: ");

                string input = Console.ReadLine();
                switch (input)
                {
                    case "1":
                        ExecuteAddRecord();
                        break;
                    case "2":
                        ExecuteViewRecords();
                        break;
                    case "3":
                        ExecuteUpdateRecord();
                        break;
                    case "4":
                        ExecuteSoftDelete();
                        break;
                    case "5":
                        ExecuteHardDelete();
                        break;
                    case "6":
                        ExecuteGenerateReport();
                        break;
                    case "7":
                        exit = true;
                        break;
                    default:
                        Console.WriteLine("Invalid option. Press any key to retry.");
                        _auditLogger.Log("Error", $"Invalid menu selection attempted: {input}");
                        Console.ReadKey();
                        break;
                }
            }
        }

        private static void ExecuteAddRecord()
        {
            Console.Clear();
            Console.WriteLine("--- ADD INVENTORY BATCH RECORD ---");

            string batchName;
            while (true)
            {
                Console.Write("Enter Batch Name: ");
                batchName = Console.ReadLine();
                if (_validator.ValidateBatchName(batchName, out string err)) break;
                Console.WriteLine(err);
            }

            string sku;
            while (true)
            {
                Console.Write("Enter SKU: ");
                sku = Console.ReadLine();
                if (_validator.ValidateSku(sku, out string err)) break;
                Console.WriteLine(err);
            }

            int quantity;
            while (true)
            {
                Console.Write("Enter Quantity: ");
                string qInput = Console.ReadLine();
                if (_validator.ValidateQuantity(qInput, out quantity, out string err)) break;
                Console.WriteLine(err);
            }

            decimal unitPrice;
            while (true)
            {
                Console.Write("Enter Unit Price: ");
                string pInput = Console.ReadLine();
                if (_validator.ValidateUnitPrice(pInput, out unitPrice, out string err)) break;
                Console.WriteLine(err);
            }

            string recordId = "BAT" + DateTime.Now.Ticks.ToString().Substring(10);
            DateTime now = DateTime.Now;

            InventoryItem newItem = new InventoryItem
            {
                RecordId = recordId,
                BatchName = batchName,
                Sku = sku,
                Quantity = quantity,
                UnitPrice = unitPrice,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true
            };
            newItem.Checksum = newItem.CalculateChecksum();

            _repository.Add(newItem);
            Console.WriteLine($"Record added successfully! Assigned ID: {recordId}");
            Console.WriteLine("Press any key to return to menu...");
            Console.ReadKey();
        }

        private static void ExecuteViewRecords()
        {
            Console.Clear();
            Console.WriteLine("--- VIEW RECORDS ---");
            Console.Write("Enter search term filter for Batch Name or SKU (Leave empty to view all): ");
            string filter = Console.ReadLine();

            var items = _repository.GetAll().Where(i => i.IsActive).ToList();

            if (!string.IsNullOrWhiteSpace(filter))
            {
                items = items.Where(i => i.BatchName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         i.Sku.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            Console.WriteLine($"\nFound {items.Count} active records:");
            Console.WriteLine($"{"ID",-8} | {"Batch Name",-20} | {"SKU",-12} | {"Qty",-6} | {"Price",-10}");
            Console.WriteLine(new string('-', 65));
            foreach (var item in items)
            {
                Console.WriteLine($"{item.RecordId,-8} | {item.BatchName,-20} | {item.Sku,-12} | {item.Quantity,-6} | {item.UnitPrice,-10:C}");
            }

            Console.WriteLine("\nPress any key to return to menu...");
            Console.ReadKey();
        }

        private static void ExecuteUpdateRecord()
        {
            Console.Clear();
            Console.WriteLine("--- UPDATE RECORD ---");
            Console.Write("Enter Record ID to update: ");
            string id = Console.ReadLine();

            var items = _repository.GetAll();
            var item = items.FirstOrDefault(i => i.RecordId.Equals(id, StringComparison.OrdinalIgnoreCase) && i.IsActive);

            if (item == null)
            {
                Console.WriteLine("Active record not found.");
                _auditLogger.Log("Error", $"Update failed. Record ID {id} not found or inactive.");
                Console.ReadKey();
                return;
            }

            Console.Write($"Enter new Batch Name (Leave empty to keep '{item.BatchName}'): ");
            string newBatchName = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(newBatchName))
            {
                if (_validator.ValidateBatchName(newBatchName, out string err)) item.BatchName = newBatchName;
                else Console.WriteLine($"Invalid entry. Keeping current: {err}");
            }

            Console.Write($"Enter new SKU (Leave empty to keep '{item.Sku}'): ");
            string newSku = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(newSku))
            {
                if (_validator.ValidateSku(newSku, out string err)) item.Sku = newSku;
                else Console.WriteLine($"Invalid entry. Keeping current: {err}");
            }

            Console.Write($"Enter new Quantity (Leave empty to keep '{item.Quantity}'): ");
            string newQtyStr = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(newQtyStr))
            {
                if (_validator.ValidateQuantity(newQtyStr, out int qty, out string err)) item.Quantity = qty;
                else Console.WriteLine($"Invalid entry. Keeping current: {err}");
            }

            Console.Write($"Enter new Unit Price (Leave empty to keep '{item.UnitPrice}'): ");
            string newPriceStr = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(newPriceStr))
            {
                if (_validator.ValidateUnitPrice(newPriceStr, out decimal price, out string err)) item.UnitPrice = price;
                else Console.WriteLine($"Invalid entry. Keeping current: {err}");
            }

            item.UpdatedAt = DateTime.Now;
            item.Checksum = item.CalculateChecksum();

            _repository.Update(item);
            Console.WriteLine("Record updated successfully!");
            Console.ReadKey();
        }

        private static void ExecuteSoftDelete()
        {
            Console.Clear();
            Console.WriteLine("--- SOFT DELETE RECORD ---");
            Console.Write("Enter Record ID to soft delete: ");
            string id = Console.ReadLine();

            var items = _repository.GetAll();
            var item = items.FirstOrDefault(i => i.RecordId.Equals(id, StringComparison.OrdinalIgnoreCase) && i.IsActive);

            if (item == null)
            {
                Console.WriteLine("Active record not found.");
                Console.ReadKey();
                return;
            }

            item.IsActive = false;
            item.UpdatedAt = DateTime.Now;
            item.Checksum = item.CalculateChecksum();

            _repository.Update(item);
            _auditLogger.Log("Delete", $"Soft deleted record ID: {id}");
            Console.WriteLine("Record marked inactive successfully.");
            Console.ReadKey();
        }

        private static void ExecuteHardDelete()
        {
            Console.Clear();
            Console.WriteLine("--- HARD DELETE RECORD ---");
            Console.Write("Enter Record ID to permanently delete: ");
            string id = Console.ReadLine();

            var items = _repository.GetAll();
            var item = items.FirstOrDefault(i => i.RecordId.Equals(id, StringComparison.OrdinalIgnoreCase));

            if (item == null)
            {
                Console.WriteLine("Record not found.");
                Console.ReadKey();
                return;
            }

            Console.Write($"Are you absolutely sure you want to PERMANENTLY delete record {id}? (y/N): ");
            string confirmation = Console.ReadLine();
            if (confirmation.Equals("y", StringComparison.OrdinalIgnoreCase))
            {
                items.RemoveAll(i => i.RecordId.Equals(id, StringComparison.OrdinalIgnoreCase));
                _repository.SaveAll(items);
                _auditLogger.Log("Delete", $"Hard deleted record ID: {id}");
                Console.WriteLine("Record permanently purged from storage.");
            }
            else
            {
                Console.WriteLine("Operation cancelled.");
            }
            Console.ReadKey();
        }

        private static void ExecuteGenerateReport()
        {
            Console.Clear();
            _reportGenerator.GenerateValuableInventoryReport();
            _auditLogger.Log("Report", "Generated Valuable Inventory Report.");
            Console.WriteLine("\nPress any key to return to menu...");
            Console.ReadKey();
        }
    }
}