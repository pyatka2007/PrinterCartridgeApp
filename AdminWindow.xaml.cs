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

namespace PrinterCartridgeApp
{
    public partial class AdminWindow : Window
    {
        public AdminWindow(int userId)
        {
            InitializeComponent();
            LoadInventory();
            LoadUserStats();
        }

        // Загрузка склада
        private void LoadInventory()
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                string query = @"
                    SELECT i.InventoryID, c.Name, c.Description, i.Quantity 
                    FROM Inventory i
                    JOIN CartridgeTypes c ON i.CartridgeTypeID = c.CartridgeTypeID";

                SqlDataAdapter adapter = new SqlDataAdapter(query, connection);
                DataTable dt = new DataTable();
                adapter.Fill(dt);
                dgInventory.ItemsSource = dt.DefaultView;
            }
        }

        // Загрузка статистики по пользователям
        private void LoadUserStats()
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                // Считаем количество заявок для каждого пользователя (у кого 0, тоже выводим)
                string query = @"
                    SELECT u.FullName, COUNT(r.RequestID) as RequestCount
                    FROM Users u
                    LEFT JOIN Requests r ON u.UserID = r.UserID
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
                // Ищем выполненные заявки за последние 7 дней и считаем, каких картриджей сколько ушло
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
    }
}