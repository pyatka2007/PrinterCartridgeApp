using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Data;
using System.Data.SqlClient;
using ClosedXML.Excel;
using Microsoft.Win32;

namespace PrinterCartridgeApp
{
    public partial class AdminWindow : Window
    {
        public AdminWindow(int userId)
        {
            InitializeComponent();
            LoadInventory();
            LoadUserStats();
            LoadDashboard();
        }

        // Загрузка склада
        private void LoadInventory()
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                string query = @"
                SELECT i.InventoryID, c.Name, c.Description, i.Quantity,
                   CASE WHEN i.Quantity <= 3 THEN 1 ELSE 0 END as IsLowStock,
                   ISNULL(consumed.Cnt, 0) as ConsumedLast4Weeks,
                   CASE 
                       WHEN ISNULL(consumed.Cnt, 0) = 0 THEN 999.0
                       ELSE CAST(i.Quantity * 4.0 / consumed.Cnt AS DECIMAL(5,1))
                   END as WeeksLeft
                FROM Inventory i
                JOIN CartridgeTypes c ON i.CartridgeTypeID = c.CartridgeTypeID
                LEFT JOIN (
                SELECT CartridgeTypeID, COUNT(*) as Cnt
                FROM Requests
                WHERE Status = N'Выполнена' AND CompletionDate >= DATEADD(day, -28, GETDATE())
                GROUP BY CartridgeTypeID
                ) consumed ON i.CartridgeTypeID = consumed.CartridgeTypeID";

                SqlDataAdapter adapter = new SqlDataAdapter(query, connection);
                DataTable dt = new DataTable();
                adapter.Fill(dt);
                dgInventory.ItemsSource = dt.DefaultView;

                // Проверка на IsLowStock = 1
                bool hasLowStock = false;
                foreach (DataRow row in dt.Rows)
                {
                    if (Convert.ToInt32(row["IsLowStock"]) == 1)
                    {
                        hasLowStock = true;
                        break;
                    }
                }

                if (hasLowStock)
                    pnlWarning.Visibility = Visibility.Visible;
                else
                    pnlWarning.Visibility = Visibility.Collapsed;
            }
        }

        // Загрузка статистики по пользователям
        private void LoadUserStats()
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                // Считаем количество заявок для каждого пользователя
                string query = @"
                SELECT u.FullName, 
                COUNT(r.RequestID) as RequestCount,
                CASE WHEN COUNT(r.RequestID) >= 3 THEN 1 ELSE 0 END as IsSuspicious
                FROM Users u
                LEFT JOIN Requests r ON u.UserID = r.UserID 
                AND r.CreatedDate >= DATEADD(day, -7, GETDATE())
                WHERE u.RoleID = 1
                GROUP BY u.FullName
                ORDER BY RequestCount DESC";

                SqlDataAdapter adapter = new SqlDataAdapter(query, connection);
                DataTable dt = new DataTable();
                adapter.Fill(dt);
                dgUserStats.ItemsSource = dt.DefaultView;
            }
        }

        // Добавить картридж на склад
        private void btnAddStock_Click(object sender, RoutedEventArgs e)
        {
            if (dgInventory.SelectedItem == null)
            {
                MessageBox.Show("Выберите картридж в таблице!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DataRowView row = (DataRowView)dgInventory.SelectedItem;
            int inventoryId = Convert.ToInt32(row["InventoryID"]);

            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                string query = "UPDATE Inventory SET Quantity = Quantity + 1 WHERE InventoryID = @id";
                using (SqlCommand cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@id", inventoryId);
                    cmd.ExecuteNonQuery();
                }
            }
            LoadInventory(); // Обновить таблицу
        }

        // Списать картридж со склада
        private void btnRemoveStock_Click(object sender, RoutedEventArgs e)
        {
            if (dgInventory.SelectedItem == null)
            {
                MessageBox.Show("Выберите картридж в таблице!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DataRowView row = (DataRowView)dgInventory.SelectedItem;
            int inventoryId = Convert.ToInt32(row["InventoryID"]);
            int currentQty = Convert.ToInt32(row["Quantity"]);

            if (currentQty <= 0)
            {
                MessageBox.Show("На складе уже 0 штук, списывать нечего!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                string query = "UPDATE Inventory SET Quantity = Quantity - 1 WHERE InventoryID = @id";
                using (SqlCommand cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@id", inventoryId);
                    cmd.ExecuteNonQuery();
                }
            }
            LoadInventory(); // Обновить таблицу
        }

        // Формирование отчета за 7 дней
        private void btnGenerateReport_Click(object sender, RoutedEventArgs e)
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                string query = @"
            SELECT c.Name as CartridgeName, COUNT(r.RequestID) as UsedCount
            FROM Requests r
            JOIN CartridgeTypes c ON r.CartridgeTypeID = c.CartridgeTypeID
            WHERE r.Status = N'Выполнена' 
              AND r.CompletionDate >= DATEADD(day, -7, GETDATE())
            GROUP BY c.Name";

                SqlDataAdapter adapter = new SqlDataAdapter(query, connection);
                DataTable dt = new DataTable();
                adapter.Fill(dt);

                if (dt.Rows.Count == 0)
                {
                    MessageBox.Show("За последние 7 дней не было выполненных заявок.", "Отчет пуст", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                dgReport.ItemsSource = dt.DefaultView;
            }
        }
        private void btnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем, есть ли данные для экспорта
            if (dgReport.ItemsSource == null || dgReport.Items.Count == 0)
            {
                MessageBox.Show("Сначала сформируйте отчет (нажмите на кнопку выше)!", "Нет данных", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Открываем окно сохранения файла
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "Excel File|*.xlsx",
                Title = "Сохранить отчет по расходу",
                FileName = "Отчет_Расход_Картриджей_" + DateTime.Now.ToString("yyyy-MM-dd")
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    // Создаем новую книгу Excel
                    using (var workbook = new XLWorkbook())
                    {
                        // Добавляем лист
                        var worksheet = workbook.Worksheets.Add("Расход за 7 дней");

                        // Заголовки таблицы
                        worksheet.Cell(1, 1).Value = "Картридж";
                        worksheet.Cell(1, 2).Value = "Израсходовано (шт)";

                        // Делаем заголовки жирными
                        worksheet.Range("A1:B1").Style.Font.Bold = true;

                        // Заполняем данными из таблицы (DataGrid)
                        int row = 2;
                        foreach (DataRowView item in dgReport.Items)
                        {
                            worksheet.Cell(row, 1).Value = item["CartridgeName"].ToString();
                            worksheet.Cell(row, 2).Value = Convert.ToInt32(item["UsedCount"]);
                            row++;
                        }

                        // Автоматически подгоняем ширину колонок
                        worksheet.Columns().AdjustToContents();

                        // Сохраняем файл
                        workbook.SaveAs(saveFileDialog.FileName);
                    }

                    MessageBox.Show("Отчет успешно сохранен в Excel!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка при сохранении файла: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        // Загрузка данных для панели приборов
        private void LoadDashboard()
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();

                // 1. Всего заявок
                string queryTotal = "SELECT COUNT(*) FROM Requests";
                using (SqlCommand cmd = new SqlCommand(queryTotal, connection))
                {
                    txtTotalRequests.Text = cmd.ExecuteScalar().ToString();
                }

                // 2. Активные заявки (Новые + В работе)
                string queryPending = "SELECT COUNT(*) FROM Requests WHERE Status IN (N'Новая', N'В работе')";
                using (SqlCommand cmd = new SqlCommand(queryPending, connection))
                {
                    txtPendingRequests.Text = cmd.ExecuteScalar().ToString();
                }

                // 3. Выполненные заявки
                string queryCompleted = "SELECT COUNT(*) FROM Requests WHERE Status = N'Выполнена'";
                using (SqlCommand cmd = new SqlCommand(queryCompleted, connection))
                {
                    txtCompletedRequests.Text = cmd.ExecuteScalar().ToString();
                }

                // 4. Общее количество картриджей на складе
                string queryStock = "SELECT ISNULL(SUM(Quantity), 0) FROM Inventory";
                using (SqlCommand cmd = new SqlCommand(queryStock, connection))
                {
                    txtTotalStock.Text = cmd.ExecuteScalar().ToString();
                }
            }
        }
    }
}