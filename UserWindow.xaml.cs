using MaterialDesignThemes.Wpf;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Windows;
using System.Collections.Generic;
using System.Windows.Controls;

namespace PrinterCartridgeApp
{
    public partial class UserWindow : Window
    {
        private int currentUserId;
        private int? editingRequestId = null; // Если null - создаем новую, если есть ID - редактируем

        public UserWindow(int userId)
        {
            InitializeComponent();
            currentUserId = userId;
            LoadCartridges();
            LoadRequests();
        }

        // 1. Загрузка картриджей в виде галочек
        private void LoadCartridges()
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                string query = "SELECT CartridgeTypeID, Name FROM CartridgeTypes";
                SqlDataAdapter adapter = new SqlDataAdapter(query, connection);
                DataTable dt = new DataTable();
                adapter.Fill(dt);

                pnlCartridges.Children.Clear();
                foreach (DataRow row in dt.Rows)
                {
                    CheckBox cb = new CheckBox
                    {
                        Content = row["Name"].ToString(),
                        Tag = Convert.ToInt32(row["CartridgeTypeID"]),
                        Margin = new Thickness(0, 5, 0, 5),
                        FontSize = 14
                    };
                    pnlCartridges.Children.Add(cb);
                }
            }
        }

        // 2. Загрузка заявок (делим на активные и выполненные)
        private void LoadRequests()
        {
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                // Используем STRING_AGG, чтобы собрать названия картриджей в одну строку
                string baseQuery = @"
                    SELECT r.RequestID, r.RoomNumber, r.CreatedDate, r.Status, r.Comment,
                           STRING_AGG(c.Name, ', ') as CartridgeNames
                    FROM Requests r
                    JOIN RequestDetails rd ON r.RequestID = rd.RequestID
                    JOIN CartridgeTypes c ON rd.CartridgeTypeID = c.CartridgeTypeID
                    WHERE r.UserID = @userId {0}
                    GROUP BY r.RequestID, r.RoomNumber, r.CreatedDate, r.Status, r.Comment
                    ORDER BY r.CreatedDate DESC";

                // Активные (Новая, В работе)
                string activeQuery = string.Format(baseQuery, "AND r.Status IN (N'Новая', N'В работе')");
                SqlDataAdapter adapterActive = new SqlDataAdapter(activeQuery, connection);
                adapterActive.SelectCommand.Parameters.AddWithValue("@userId", currentUserId);
                DataTable dtActive = new DataTable();
                adapterActive.Fill(dtActive);
                dgActiveRequests.ItemsSource = dtActive.DefaultView;

                // Выполненные
                string completedQuery = string.Format(baseQuery, "AND r.Status = N'Выполнена'");
                SqlDataAdapter adapterCompleted = new SqlDataAdapter(completedQuery, connection);
                adapterCompleted.SelectCommand.Parameters.AddWithValue("@userId", currentUserId);
                DataTable dtCompleted = new DataTable();
                adapterCompleted.Fill(dtCompleted);
                dgCompletedRequests.ItemsSource = dtCompleted.DefaultView;
            }
        }

        // 3. Отправка или Сохранение изменений
        private void btnSubmitRequest_Click(object sender, RoutedEventArgs e)
        {
            // Собираем выбранные ID картриджей
            List<int> selectedCartridges = new List<int>();
            foreach (CheckBox cb in pnlCartridges.Children)
            {
                if (cb.IsChecked == true) selectedCartridges.Add((int)cb.Tag);
            }

            if (selectedCartridges.Count == 0 || string.IsNullOrWhiteSpace(txtRoom.Text))
            {
                MessageBox.Show("Выберите хотя бы один картридж и укажите кабинет!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string room = txtRoom.Text.Trim();

            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                using (SqlTransaction transaction = connection.BeginTransaction())
                {
                    try
                    {
                        if (editingRequestId == null) // НОВАЯ ЗАЯВКА
                        {
                            // Анти-спам
                            string checkQuery = "SELECT COUNT(*) FROM Requests WHERE UserID = @userId AND RoomNumber = @room AND Status IN (N'Новая', N'В работе')";
                            using (SqlCommand checkCmd = new SqlCommand(checkQuery, connection, transaction))
                            {
                                checkCmd.Parameters.AddWithValue("@userId", currentUserId);
                                checkCmd.Parameters.AddWithValue("@room", room);
                                if ((int)checkCmd.ExecuteScalar() > 0)
                                {
                                    MessageBox.Show("По вашему кабинету уже есть активная заявка!", "Анти-спам", MessageBoxButton.OK, MessageBoxImage.Stop);
                                    transaction.Rollback();
                                    return;
                                }
                            }

                            // 1. Создаем саму заявку
                            string insertReq = "INSERT INTO Requests (UserID, RoomNumber, Status) OUTPUT INSERTED.RequestID VALUES (@userId, @room, N'Новая')";
                            int newRequestId;
                            using (SqlCommand cmdReq = new SqlCommand(insertReq, connection, transaction))
                            {
                                cmdReq.Parameters.AddWithValue("@userId", currentUserId);
                                cmdReq.Parameters.AddWithValue("@room", room);
                                newRequestId = (int)cmdReq.ExecuteScalar();
                            }

                            // 2. Добавляем детали (картриджи)
                            foreach (int cartId in selectedCartridges)
                            {
                                string insertDetail = "INSERT INTO RequestDetails (RequestID, CartridgeTypeID) VALUES (@reqId, @cartId)";
                                using (SqlCommand cmdDet = new SqlCommand(insertDetail, connection, transaction))
                                {
                                    cmdDet.Parameters.AddWithValue("@reqId", newRequestId);
                                    cmdDet.Parameters.AddWithValue("@cartId", cartId);
                                    cmdDet.ExecuteNonQuery();
                                }
                            }
                            MessageBox.Show("Заявка успешно создана!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else // РЕДАКТИРОВАНИЕ СУЩЕСТВУЮЩЕЙ
                        {
                            // 1. Обновляем кабинет в заявке
                            string updateReq = "UPDATE Requests SET RoomNumber = @room WHERE RequestID = @id";
                            using (SqlCommand cmdReq = new SqlCommand(updateReq, connection, transaction))
                            {
                                cmdReq.Parameters.AddWithValue("@room", room);
                                cmdReq.Parameters.AddWithValue("@id", editingRequestId);
                                cmdReq.ExecuteNonQuery();
                            }

                            // 2. Удаляем старые детали и добавляем новые
                            string deleteDetails = "DELETE FROM RequestDetails WHERE RequestID = @id";
                            using (SqlCommand cmdDel = new SqlCommand(deleteDetails, connection, transaction))
                            {
                                cmdDel.Parameters.AddWithValue("@id", editingRequestId);
                                cmdDel.ExecuteNonQuery();
                            }

                            foreach (int cartId in selectedCartridges)
                            {
                                string insertDetail = "INSERT INTO RequestDetails (RequestID, CartridgeTypeID) VALUES (@reqId, @cartId)";
                                using (SqlCommand cmdDet = new SqlCommand(insertDetail, connection, transaction))
                                {
                                    cmdDet.Parameters.AddWithValue("@reqId", editingRequestId);
                                    cmdDet.Parameters.AddWithValue("@cartId", cartId);
                                    cmdDet.ExecuteNonQuery();
                                }
                            }
                            MessageBox.Show("Заявка успешно изменена!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                        }

                        transaction.Commit();
                        ResetForm();
                        LoadRequests();
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        MessageBox.Show("Ошибка: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        // 4. Логика редактирования (двойной клик по активной заявке)
        private void dgActiveRequests_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (dgActiveRequests.SelectedItem == null) return;

            DataRowView row = (DataRowView)dgActiveRequests.SelectedItem;
            if (row["Status"].ToString() != "Новая")
            {
                MessageBox.Show("Редактировать можно только заявки со статусом 'Новая'.", "Внимание", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            editingRequestId = Convert.ToInt32(row["RequestID"]);
            txtFormTitle.Text = "Редактирование заявки";
            txtRoom.Text = row["RoomNumber"].ToString();
            btnSubmitRequest.Content = "СОХРАНИТЬ ИЗМЕНЕНИЯ";
            btnCancelEdit.Visibility = Visibility.Visible;

            // Снимаем все галочки
            foreach (CheckBox cb in pnlCartridges.Children) cb.IsChecked = false;

            // Ставим галочки на те картриджи, которые были в заявке
            using (SqlConnection connection = new SqlConnection(DbHelper.ConnectionString))
            {
                connection.Open();
                string query = "SELECT CartridgeTypeID FROM RequestDetails WHERE RequestID = @id";
                using (SqlCommand cmd = new SqlCommand(query, connection))
                {
                    cmd.Parameters.AddWithValue("@id", editingRequestId);
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int cartId = Convert.ToInt32(reader["CartridgeTypeID"]);
                            foreach (CheckBox cb in pnlCartridges.Children)
                            {
                                if ((int)cb.Tag == cartId) cb.IsChecked = true;
                            }
                        }
                    }
                }
            }
        }

        // 5. Отмена редактирования
        private void btnCancelEdit_Click(object sender, RoutedEventArgs e)
        {
            ResetForm();
        }

        private void ResetForm()
        {
            editingRequestId = null;
            txtFormTitle.Text = "Новая заявка";
            txtRoom.Clear();
            btnSubmitRequest.Content = "ОТПРАВИТЬ ЗАЯВКУ";
            btnCancelEdit.Visibility = Visibility.Collapsed;
            foreach (CheckBox cb in pnlCartridges.Children) cb.IsChecked = false;
        }

        // Тема (без изменений)
        private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(BaseTheme.Dark);
            paletteHelper.SetTheme(theme);
            ThemeIcon.Kind = PackIconKind.WeatherSunny;
        }

        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(BaseTheme.Light);
            paletteHelper.SetTheme(theme);
            ThemeIcon.Kind = PackIconKind.WeatherNight;
        }
    }
}