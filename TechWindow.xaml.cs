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
    public partial class TechWindow : Window
    {
        private int currentTechId;

        public TechWindow(int userId)
        {
            InitializeComponent();
            currentTechId = userId;
            LoadRequests();
        }

        // Загрузка всех заявок, кроме отклоненных
        private void LoadRequests()
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                string query = @"
                    SELECT r.RequestID, u.FullName as UserName, r.RoomNumber, 
                           c.Name as CartridgeName, r.CreatedDate, r.Status, r.Comment
                    FROM Requests r
                    JOIN Users u ON r.UserID = u.UserID
                    JOIN CartridgeTypes c ON r.CartridgeTypeID = c.CartridgeTypeID
                    WHERE r.Status != N'Отклонена'
                    ORDER BY 
                        CASE r.Status 
                            WHEN N'Новая' THEN 1 
                            WHEN N'В работе' THEN 2 
                            ELSE 3 
                        END, r.CreatedDate DESC";

                SqlDataAdapter adapter = new SqlDataAdapter(query, connection);
                DataTable dt = new DataTable();
                adapter.Fill(dt);
                dgRequests.ItemsSource = dt.DefaultView;
            }
        }

        // Взять заявку в работу
        private void btnTakeWork_Click(object sender, RoutedEventArgs e)
        {
            if (dgRequests.SelectedItem == null)
            {
                MessageBox.Show("Выберите заявку из таблицы!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DataRowView row = (DataRowView)dgRequests.SelectedItem;
            string currentStatus = row["Status"].ToString();
            int requestId = Convert.ToInt32(row["RequestID"]);

            if (currentStatus == "Выполнена")
            {
                MessageBox.Show("Эта заявка уже выполнена!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                string query = "UPDATE Requests SET Status = N'В работе', TechnicianID = @techId WHERE RequestID = @id";
                using (SqlCommand cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@techId", currentTechId);
                    cmd.Parameters.AddWithValue("@id", requestId);
                    cmd.ExecuteNonQuery();
                }
            }
            LoadRequests(); // Обновляем таблицу
        }

        // Выполнить заявку (списать картридж со склада)
        private void btnComplete_Click(object sender, RoutedEventArgs e)
        {
            if (dgRequests.SelectedItem == null)
            {
                MessageBox.Show("Выберите заявку из таблицы!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DataRowView row = (DataRowView)dgRequests.SelectedItem;
            string currentStatus = row["Status"].ToString();
            int requestId = Convert.ToInt32(row["RequestID"]);
            string cartridgeName = row["CartridgeName"].ToString();

            if (currentStatus == "Выполнена")
            {
                MessageBox.Show("Эта заявка уже выполнена!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Проверка остатков на складе
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();

                // Узнаем ID картриджа из заявки
                string getCartridgeQuery = "SELECT CartridgeTypeID FROM Requests WHERE RequestID = @id";
                int cartridgeId = 0;
                using (SqlCommand cmd = new SqlCommand(getCartridgeQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@id", requestId);
                    cartridgeId = (int)cmd.ExecuteScalar();
                }

                // Проверяем, есть ли картридж на складе
                string checkStockQuery = "SELECT Quantity FROM Inventory WHERE CartridgeTypeID = @cartId";
                int stock = 0;
                using (SqlCommand cmd = new SqlCommand(checkStockQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@cartId", cartridgeId);
                    stock = (int)cmd.ExecuteScalar();
                }

                if (stock <= 0)
                {
                    MessageBox.Show($"На складе нет картриджей {cartridgeName}! Пополните склад перед выполнением.", "Ошибка склада", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Используем транзакцию: либо списываем и закрываем заявку, либо откатываем всё
                using (SqlTransaction transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // 1. Списываем 1 шт со склада
                        string updateStockQuery = "UPDATE Inventory SET Quantity = Quantity - 1 WHERE CartridgeTypeID = @cartId";
                        using (SqlCommand cmd = new SqlCommand(updateStockQuery, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@cartId", cartridgeId);
                            cmd.ExecuteNonQuery();
                        }

                        // 2. Закрываем заявку
                        string updateRequestQuery = @"
                            UPDATE Requests 
                            SET Status = N'Выполнена', 
                                TechnicianID = @techId, 
                                CompletionDate = GETDATE(), 
                                Comment = @comment 
                            WHERE RequestID = @id";

                        using (SqlCommand cmd = new SqlCommand(updateRequestQuery, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@techId", currentTechId);
                            cmd.Parameters.AddWithValue("@comment", txtComment.Text.Trim());
                            cmd.Parameters.AddWithValue("@id", requestId);
                            cmd.ExecuteNonQuery();
                        }

                        transaction.Commit(); // Подтверждаем изменения
                        MessageBox.Show("Заявка выполнена, картридж списан со склада!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                        txtComment.Clear();
                        LoadRequests();
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback(); // Откатываем, если что-то пошло не так
                        MessageBox.Show("Ошибка при выполнении заявки: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
    }
}