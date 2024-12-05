using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;

namespace Server
{
    public partial class frm_Server : Form
    {
        private const string CLOSE_COMMAND = "0";
        private const string SEND_MESSAGE = "1";
        private const string SEND_IMAGE = "2";
        private const string SEND_FILE = "3";
        private const string SEND_EMOJI = "4";
        private const string FORWARD_MESSAGE = "5";

        private List<Socket> clients;
        private List<Panel> panelViews;
        private List<byte[]> buffers;
        private Socket server;
        private bool listening = false;
        private const int PORT = 5555;
        private delegate void SocketAcceptedHandler(Socket socket);
        private event SocketAcceptedHandler socketAccepted;
        private int currentClientIndex = 0;
        private string prevMessage = "";
        private Image prevEmoji = null;
        private bool hasClient = false;
        private String savePath;
        private String fileName;
        private DockStyle dockStyle;
        private string filePath;

        public frm_Server()
        {
            InitializeComponent();

            CheckForIllegalCrossThreadCalls = false;
            clients = new List<Socket>();
            panelViews = new List<Panel>();
            buffers = new List<byte[]>();
            socketAccepted += new SocketAcceptedHandler(socketAcceptedCallback);
            server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        private void socketAcceptedCallback(Socket socket)
        {
            clients.Add(socket);
            BeginInvoke((Action)(() =>
            {
                if (!hasClient) pnlMain.Controls.Clear();

                CheckBox checkBox = createChatClient(socket.RemoteEndPoint.ToString());
                Panel panel = createChatPanelViewItem();

                hasClient = true;

                if (clients.Count == 1)
                {
                    lblUsername.Text = checkBox.Text;
                }

                pnlSidebar.Controls.Add(checkBox);
                panelViews.Add(panel);

                pnlMain.Controls.Add(panel);

                byte[] buffer = new byte[1024 * 20];
                buffers.Add(buffer);
                socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, receiveCallback, socket);
            }));
        }
        private void relayMessageToClients(string dataType, string message, int senderIndex)
        {
            byte[] data = Encoding.UTF8.GetBytes(dataType + message);
            foreach (Socket client in clients)
            {
                try
                {
                    if (clients.IndexOf(client) != senderIndex)
                    {
                        client.Send(data);
                    }
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"SocketException: {ex.Message}");
                    clients.Remove(client);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception: {ex.Message}");
                }
            }
        }

        private void relayMessageToClients(string dataType, byte[] data, int senderIndex)
        {
            byte[] dataTypeBytes = Encoding.UTF8.GetBytes(dataType);
            byte[] mergedData = dataTypeBytes.Concat(data).ToArray();
            foreach (Socket client in clients)
            {
                try
                {
                    if (clients.IndexOf(client) != senderIndex)
                    {
                        client.Send(mergedData);
                    }
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"SocketException: {ex.Message}");
                    clients.Remove(client);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception: {ex.Message}");
                }
            }
        }

        private void receiveCallback(IAsyncResult ar)
        {
            Socket client = (Socket)ar.AsyncState;
            try
            {
                int clientIndex = getClientIndex(client);
                int bytesRead = client.EndReceive(ar);
                if (bytesRead > 0)
                {
                    byte[] receivedData = new byte[bytesRead];
                    Array.Copy(buffers[clientIndex], receivedData, bytesRead); // Use the correct buffer

                    string dataType = Encoding.UTF8.GetString(receivedData, 0, 1);

                    switch (dataType)
                    {
                        case CLOSE_COMMAND:
                            handleClientDisconnect(client);
                            return;
                        case SEND_MESSAGE:
                        case FORWARD_MESSAGE:
                            string message = Encoding.UTF8.GetString(receivedData, 1, bytesRead - 1);
                            relayMessageToClients(SEND_MESSAGE, message, clientIndex);

                            BeginInvoke((Action)(() =>
                            {
                                try
                                {
                                    Label labelTime = createChatTime();
                                    Panel panelMessage = createChatMessage(message, DockStyle.Left);
                                    panelMessage.Tag = labelTime;

                                    panelViews[clientIndex].Controls.Add(labelTime);
                                    labelTime.BringToFront();
                                    panelViews[clientIndex].Controls.Add(panelMessage);
                                    panelMessage.BringToFront();
                                    panelViews[clientIndex].ScrollControlIntoView(panelMessage);
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("Error handling message: " + ex.Message);
                                }
                            }));
                            break;
                        case SEND_IMAGE:
                            relayMessageToClients(SEND_IMAGE, receivedData.Skip(1).ToArray(), clientIndex);
                            BeginInvoke((Action)(() =>
                            {
                                try
                                {
                                    ImageConverter convertData = new ImageConverter();
                                    Image image = null;
                                    using (MemoryStream ms = new MemoryStream(receivedData.Skip(1).ToArray()))
                                    {
                                        image = Image.FromStream(ms);
                                    }

                                    Label labelTime = createChatTime();
                                    Panel panelImage = createChatImage(image, DockStyle.Left);
                                    panelImage.Tag = labelTime;

                                    panelViews[clientIndex].Controls.Add(labelTime);
                                    labelTime.BringToFront();
                                    panelViews[clientIndex].Controls.Add(panelImage);
                                    panelImage.BringToFront();
                                    panelViews[clientIndex].ScrollControlIntoView(panelImage);
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("Error converting image: " + ex.Message);
                                }
                            }));
                            break;
                        case SEND_FILE:
                            relayMessageToClients(SEND_FILE, receivedData.Skip(1).ToArray(), clientIndex);
                            BeginInvoke((Action)(() =>
                            {
                                try
                                {
                                    string fileName = Encoding.UTF8.GetString(receivedData, 1, 256).Trim();
                                    byte[] fileData = receivedData.Skip(257).ToArray();

                                    string filePath = Path.Combine("ReceivedFiles", fileName);
                                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)); // Ensure the directory exists
                                    File.WriteAllBytes(filePath, fileData);

                                    Label labelTime = createChatTime();
                                    Panel panelFile = createChatFile(fileName, DockStyle.Left);
                                    panelFile.Tag = fileName;
                                    panelViews[clientIndex].Controls.Add(labelTime);
                                    labelTime.BringToFront();
                                    panelViews[clientIndex].Controls.Add(panelFile);
                                    panelFile.BringToFront();
                                    panelViews[clientIndex].ScrollControlIntoView(panelFile);
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("Error handling file: " + ex.Message);
                                }
                            }));
                            break;
                        case SEND_EMOJI:
                            relayMessageToClients(SEND_EMOJI , receivedData.Skip(1).ToArray(), clientIndex);
                            BeginInvoke((Action)(() =>
                            {
                                try
                                {
                                    //ImageConverter convertData = new ImageConverter();
                                    //Image image = null;
                                    //using (MemoryStream ms = new MemoryStream(receivedData.Skip(1).ToArray()))
                                    //{
                                    //    image = Image.FromStream(ms);
                                    //}

                                    byte[] data = receivedData.Skip(1).ToArray();

                                    // Kiểm tra xem dữ liệu có phải là hình ảnh hợp lệ
                                    bool isValidImage = false;
                                    Image image = null;
                                    try
                                    {
                                        using (MemoryStream ms = new MemoryStream(data))
                                        {
                                            image = Image.FromStream(ms);
                                            isValidImage = true;
                                        }
                                    }
                                    catch
                                    {
                                        isValidImage = false; // Không phải hình ảnh hợp lệ
                                    }

                                    if (isValidImage && image != null)
                                    {

                                        Label labelTime = createChatTime();
                                        Panel panelEmoji = createChatEmoji(image, DockStyle.Left);
                                        panelEmoji.Tag = labelTime;

                                        pnlMain.Controls[0].Controls.Add(labelTime);
                                        labelTime.BringToFront();
                                        pnlMain.Controls[0].Controls.Add(panelEmoji);
                                        panelEmoji.BringToFront();
                                        (pnlMain.Controls[0] as Panel).ScrollControlIntoView(panelEmoji);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show("Error converting emoji: " + ex.Message);
                                }
                            }));
                            break;
                    }

                    buffers[clientIndex] = new byte[1024 * 100]; // Reset the buffer
                    client.BeginReceive(buffers[clientIndex], 0, buffers[clientIndex].Length, SocketFlags.None, receiveCallback, client); // Use the correct buffer
                }
                else
                {
                    client.Close();
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Error receiving data from server");
            }
        }


        private void handleClientDisconnect(Socket client)
        {
            int index = getClientIndex(client);
            if (index != -1)
            {
                BeginInvoke((Action)(() =>
                {
                    pnlSidebar.Controls[currentClientIndex].BackColor = Color.White;
                    pnlSidebar.Controls.RemoveAt(index);
                    pnlMain.Controls.RemoveAt(index);
                    panelViews.RemoveAt(index);
                    clients.RemoveAt(index);
                    buffers.RemoveAt(index);
                    setCurrentClient();
                    client.Close();
                }));
            }
        }

        private void frm_Server_Load(object sender, EventArgs e)
        {
            start();
            loadEmoji();
        }

        private void start()
        {
            if (listening) return;
            server.Bind(new IPEndPoint(IPAddress.Any, PORT));
            server.Listen(10);
            server.BeginAccept(beginAcceptCallback, null);

            listening = true;
        }

        private void stop()
        {
            if (!listening) return;
            server.Close();
            server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            listening = false;
        }

        private void beginAcceptCallback(IAsyncResult ar)
        {
            try
            {
                Socket client = server.EndAccept(ar);

                if (socketAccepted != null)
                {
                    socketAccepted(client);
                }

                server.BeginAccept(beginAcceptCallback, null);

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private Panel createChatPanelViewItem()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.ForeColor = Color.Gray;
            panel.Padding = new Padding(20, 10, 20, 20);
            panel.BackColor = Color.White;
            panel.AutoScroll = true;
            panel.Visible = currentClientIndex == 0 ? true : false;
            return panel;
        }

        private Panel createChatFile(string fileName, DockStyle dock)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Top;
            panel.Tag = fileName;

            Label label = new Label();
            label.Text = fileName;
            label.Dock = dock;
            label.MaximumSize = new Size(300, 500);
            label.AutoSize = true;
            label.BorderStyle = BorderStyle.FixedSingle;
            label.Font = new Font(label.Font.FontFamily, 12);
            label.ForeColor = dock == DockStyle.Right ? Color.Gray : Color.Black;
            label.Padding = new Padding(10);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.BackColor = dock == DockStyle.Right ? Color.FromArgb(245, 228, 145) : Color.FromArgb(3, 221, 17);
            label.AutoEllipsis = true;

            label.Click += label_Click;

            PictureBox ptbDelete = new PictureBox();
            ptbDelete.Size = new Size(18, 18);
            ptbDelete.Image = Properties.Resources.icons8_delete_50;
            ptbDelete.SizeMode = PictureBoxSizeMode.StretchImage;
            ptbDelete.Dock = DockStyle.Bottom;

            Panel pnlDelete = new Panel();
            pnlDelete.Dock = dock;
            pnlDelete.Controls.Add(ptbDelete);
            pnlDelete.Size = new Size(28, pnlDelete.Height);
            pnlDelete.Padding = new Padding(5, 5, 5, 10);

            panel.Controls.Add(label);
            panel.Height = label.Height;
            panel.Controls.Add(pnlDelete);
            pnlDelete.BringToFront();

            ptbDelete.Click += ptbDelete_Click;
            panel.Click += panelFile_Click;

            return panel;
        }

        private void label_Click(object sender, EventArgs e)
        {
            Label label = sender as Label;
            string fileName = label?.Text;

            if (!string.IsNullOrEmpty(fileName))
            {
                string filePath = Path.Combine("ReceivedFiles", fileName);

                if (File.Exists(filePath))
                {
                    SaveFileDialog saveFileDialog = new SaveFileDialog
                    {
                        FileName = fileName,
                        Filter = "All Files|*.*"
                    };

                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        try
                        {
                            File.Copy(filePath, saveFileDialog.FileName, true);
                            MessageBox.Show("Tệp đã được tải xuống thành công.", "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Đã xảy ra lỗi khi tải tệp: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
                else
                {
                    MessageBox.Show("Tệp không tồn tại.", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                MessageBox.Show("Tên tệp không hợp lệ.", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }


        private void panelFile_Click(object sender, EventArgs e)
        {
            Panel panel = sender as Panel;
            string fileName = panel.Tag as string;

            if (!string.IsNullOrEmpty(fileName))
            {
                string filePath = Path.Combine("ReceivedFiles", fileName);

                if (File.Exists(filePath))
                {
                    SaveFileDialog saveFileDialog = new SaveFileDialog();
                    saveFileDialog.FileName = fileName;
                    saveFileDialog.Filter = "All Files|*.*";

                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        File.Copy(filePath, saveFileDialog.FileName, true);
                        MessageBox.Show("File downloaded successfully.");
                    }
                }
                else
                {
                    MessageBox.Show("File not found.");
                }
            }
            else
            {
                MessageBox.Show("File name is null or empty.");
            }
        }

        private void sendFile(string filePath)
        {
            try
            {
                string fileName = Path.GetFileName(filePath);
                byte[] fileData = File.ReadAllBytes(filePath);

                byte[] dataType = Encoding.UTF8.GetBytes(SEND_FILE);
                byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName.PadRight(256));
                byte[] mergedArray = dataType.Concat(fileNameBytes).Concat(fileData).ToArray();

                foreach (Socket client in clients)
                {
                    client.Send(mergedArray);
                }

                Label labelTime = createChatTime();
                Panel panelFile = createChatFile(fileName, DockStyle.Right);
                panelFile.Tag = fileName;

                panelViews[currentClientIndex].Controls.Add(labelTime);
                labelTime.BringToFront();
                panelViews[currentClientIndex].Controls.Add(panelFile);
                panelFile.BringToFront();
                panelViews[currentClientIndex].ScrollControlIntoView(panelFile);

                MessageBox.Show("Tệp đã được gửi thành công.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending file '{Path.GetFileName(filePath)}': {ex.Message}");
            }
        }
        private Panel createChatMessage(string message, DockStyle dock)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Top;

            Label label = new Label();
            label.Text = message;
            label.Dock = dock;
            label.MaximumSize = new Size(300, 500);
            label.AutoSize = true;
            label.BorderStyle = BorderStyle.FixedSingle;
            label.Font = new Font(label.Font.FontFamily, 12);
            label.ForeColor = dock == DockStyle.Right ? Color.Gray : Color.Black;
            label.Padding = new Padding(10);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.BackColor = dock == DockStyle.Right ? Color.FromArgb(245, 228, 145) : Color.FromArgb(3, 221, 17);
            label.AutoEllipsis = true;

            PictureBox ptbDelete = new PictureBox();
            ptbDelete.Size = new Size(18, 18);
            ptbDelete.Image = Properties.Resources.icons8_delete_50;
            ptbDelete.SizeMode = PictureBoxSizeMode.StretchImage;
            ptbDelete.Dock = DockStyle.Bottom;

        

            Panel pnlDelete = new Panel();
            pnlDelete.Dock = dock;
            pnlDelete.Controls.Add(ptbDelete);

            pnlDelete.Size = new Size(28, pnlDelete.Height);
            pnlDelete.Padding = new Padding(5, 5, 5, 10);

            panel.Controls.Add(label);
            panel.Height = label.Height;
            panel.Controls.Add(pnlDelete);
            pnlDelete.BringToFront();

            ptbDelete.Click += ptbDelete_Click;


            return panel;
        }
        private void ptbDelete_Click(object sender, EventArgs e)
        {
            try
            {
                // Hộp thoại xác nhận xóa
                DialogResult dialogResult = MessageBox.Show(
                    "Bạn có muốn xóa tin nhắn này không?",
                    "Xác nhận xóa",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (dialogResult == DialogResult.Yes)
                {
                    PictureBox ptbDelete = sender as PictureBox;

                    if (ptbDelete == null || ptbDelete.Parent == null || ptbDelete.Parent.Parent == null)
                    {
                        MessageBox.Show("Lỗi: Không tìm thấy đối tượng cần xóa.", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    Control topParent = ptbDelete.Parent.Parent;
                    object tagValue = topParent.Tag;

                    if (panelViews[currentClientIndex].Controls.Contains(topParent))
                        panelViews[currentClientIndex].Controls.Remove(topParent);

                    if (tagValue is Control time && panelViews[currentClientIndex].Controls.Contains(time))
                        panelViews[currentClientIndex].Controls.Remove(time);

                    if (tagValue is string filePath && File.Exists(filePath))
                    {
                        try
                        {
                            File.Delete(filePath); // Xóa file nếu tồn tại
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Không thể xóa file: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        private Panel createChatEmoji(Image image, DockStyle dock)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Top;

            PictureBox picture = new PictureBox();
            picture.AutoSize = false;
            picture.Dock = dock;
            picture.Size = new Size(50, 50);
            picture.SizeMode = PictureBoxSizeMode.StretchImage;
            picture.Image = image;
            picture.BackColor = dock == DockStyle.Right ? Color.FromArgb(0, 145, 255) : Color.FromArgb(229, 239, 255);
            picture.Padding = new Padding(10);

            PictureBox ptbDelete = new PictureBox();
            ptbDelete.Size = new Size(18, 18);
            ptbDelete.Image = Properties.Resources.icons8_delete_50;
            ptbDelete.SizeMode = PictureBoxSizeMode.StretchImage;
            ptbDelete.Dock = DockStyle.Bottom;

            Panel pnlDelete = new Panel();
            pnlDelete.Dock = dock;
            pnlDelete.Controls.Add(ptbDelete);
            pnlDelete.Size = new Size(28, pnlDelete.Height);
            pnlDelete.Padding = new Padding(5, 5, 5, 10);

            panel.Controls.Add(picture);
            panel.Size = new Size(50, 50);
            panel.Controls.Add(pnlDelete);
            pnlDelete.BringToFront();

            ptbDelete.Click += ptbDelete_Click;

            return panel;
        }

        private Panel createChatImage(Image image, DockStyle dock)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Top;

            PictureBox picture = new PictureBox();
            picture.AutoSize = false;
            picture.Dock = dock;
            picture.Size = calculateAspectRatio(image.Size, 300);
            picture.SizeMode = PictureBoxSizeMode.StretchImage;
            picture.Image = image;
            picture.MaximumSize = new Size(300, 700);
            picture.BackColor = Color.White;

            PictureBox ptbDelete = new PictureBox();
            ptbDelete.Size = new Size(18, 18);
            ptbDelete.Image = Properties.Resources.icons8_delete_50;
            ptbDelete.SizeMode = PictureBoxSizeMode.StretchImage;
            ptbDelete.Dock = DockStyle.Bottom;

            Panel pnlDelete = new Panel();
            pnlDelete.Dock = dock;
            pnlDelete.Controls.Add(ptbDelete);
            pnlDelete.Size = new Size(28, pnlDelete.Height);
            pnlDelete.Padding = new Padding(5, 5, 5, 10);

            panel.Controls.Add(picture);
            panel.Size = calculateAspectRatio(image.Size, 300);
            panel.Controls.Add(pnlDelete);
            pnlDelete.BringToFront();

            ptbDelete.Click += ptbDelete_Click;

            return panel;
        }

        public Size calculateAspectRatio(Size originalSize, int targetWidth)
        {
            int targetHeight = (targetWidth * originalSize.Height) / originalSize.Width;

            return new Size(targetWidth, targetHeight);
        }

        private Label createChatTime()
        {
            Label label = new Label();
            label.Text = getCurrentDateTime();
            label.AutoSize = false;
            label.Dock = DockStyle.Top;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Font = new Font(label.Font.FontFamily, 9);
            label.Size = new Size(0, 45);
            label.ForeColor = Color.FromArgb(140, 140, 140);

            return label;
        }

        private CheckBox createChatClient(string chatName)
        {
            CheckBox checkBox = new CheckBox();
            checkBox.Text = chatName;
            checkBox.AutoSize = false;
            checkBox.Dock = DockStyle.Top;
            checkBox.Size = new Size(0, 50);
            checkBox.ForeColor = Color.Black;
            checkBox.Padding = new Padding(10);
            checkBox.TextAlign = ContentAlignment.MiddleLeft;
            checkBox.BackColor = clients.Count == 1 ? Color.FromArgb(229, 239, 255) : Color.White;
            checkBox.Checked = clients.Count == 1;
            checkBox.Click += chatClient_Click;
            return checkBox;
        }

        private void chatClient_Click(object sender, EventArgs e)
        {
            CheckBox chatLabel = sender as CheckBox;

            int index = pnlSidebar.Controls.GetChildIndex(chatLabel);

            if (index == currentClientIndex) return;

            pnlSidebar.Controls[currentClientIndex].BackColor = Color.White;

            chatLabel.BackColor = Color.FromArgb(229, 239, 255);

            lblUsername.Text = chatLabel.Text;


            panelViews[currentClientIndex].Visible = false;
            currentClientIndex = index;
            panelViews[currentClientIndex].Visible = true;
        }

        private void setCurrentClient()
        {
            if (clients.Count == 0)
            {
                hasClient = false;
                currentClientIndex = 0;
                lblUsername.Text = "";
                pnlMain.Controls.Add(createEmptyLabel());
            }
            else
            {
                currentClientIndex = pnlSidebar.Controls.Count - 1;
                panelViews[currentClientIndex].Visible = true;
                (pnlSidebar.Controls[currentClientIndex] as CheckBox).Checked = true;
                pnlSidebar.Controls[currentClientIndex].BackColor = Color.FromArgb(229, 239, 255);
            }
        }

        private Label createEmptyLabel()
        {
            Label label = new Label();
            label.Text = "Không có người dùng nào để nhắn tin";
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Font = new Font(label.Font.FontFamily, 12);
            return label;
        }

        private int getClientIndex(Socket socket)
        {
            return clients.IndexOf(socket);
        }

        private void frm_Server_FormClosing(object sender, FormClosingEventArgs e)
        {
            stop();
        }

        private string getCurrentDateTime()
        {
            return DateTime.Now.ToString("HH:mm:ss dd/MM/yyyy");
        }

        private void txtMessage_TextChanged(object sender, EventArgs e)
        {
            if (txtMessage.Text.Length > 0)
            {
                lblHint.Visible = false;
            }
            else
            {
                lblHint.Visible = true;
            }
        }

        private void loadEmoji()
        {
            string[] files = Directory.GetFiles("Emojis");
            foreach (string file in files)
            {
                PictureBox picture = new PictureBox();
                picture.Size = new Size(28, 28);
                picture.Image = Image.FromFile(file);
                picture.SizeMode = PictureBoxSizeMode.StretchImage;
                picture.Padding = new Padding(2);
                pnlEmoji.Controls.Add(picture);

                picture.Click += ptbEmojiItem_Click;
            }
        }

        private void ptbEmojiItem_Click(object sender, EventArgs e)
        {
            prevEmoji = (sender as PictureBox).Image;
            pnlEmoji.Visible = false;
            try
            {
                byte[] dataType = Encoding.UTF8.GetBytes(SEND_EMOJI);

                MemoryStream ms = new MemoryStream();
                prevEmoji.Save(ms, ImageFormat.Png);

                byte[] imageData = ms.ToArray();

                byte[] mergedArray = dataType.Concat(imageData).ToArray();

                int numberOfRecipients = 0;
                for (int i = 0; i < pnlSidebar.Controls.Count; i++)
                {
                    if ((pnlSidebar.Controls[i] as CheckBox).Checked)
                    {
                        clients[i].Send(mergedArray);

                        Label labelTime = createChatTime();
                        Panel panelEmoji = createChatEmoji(prevEmoji, DockStyle.Right);
                        panelEmoji.Tag = labelTime;

                        panelViews[i].Controls.Add(labelTime);
                        labelTime.BringToFront();
                        panelViews[i].Controls.Add(panelEmoji);
                        panelEmoji.BringToFront();
                        panelViews[i].ScrollControlIntoView(panelEmoji);
                        numberOfRecipients++;
                    }
                }

                if (numberOfRecipients == 0)
                {
                    MessageBox.Show("Vui lòng chọn người để gửi ảnh");
                }

                ms.Close();
            }
            catch (Exception)
            {
                MessageBox.Show("Lỗi gửi hình ảnh");
            }
        }

        private void lblHint_Click(object sender, EventArgs e)
        {
            lblHint.Visible = false;
            txtMessage.Visible = true;
            txtMessage.Focus();
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            if (clients.Count == 0)
            {
                MessageBox.Show("Chưa có người dùng để chat");
                return;
            }

            if (txtMessage.Text.Length == 0)
            {
                MessageBox.Show("Vui lòng nhập nội dung tin nhắn");
                return;
            }

            int numberOfRecipients = 0;
            prevMessage = txtMessage.Text;

            for (int i = 0; i < pnlSidebar.Controls.Count; i++)
            {
                if ((pnlSidebar.Controls[i] as CheckBox).Checked)
                {
                    sendMessage(SEND_MESSAGE + prevMessage, i);
                    numberOfRecipients++;
                }
            }

            if (numberOfRecipients == 0)
            {
                MessageBox.Show("Vui lòng chọn người gửi tin nhắn");
            }

        }

        private void sendMessage(string message, int index)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                clients[index].BeginSend(data, 0, data.Length, SocketFlags.None, sendCallback, clients[index]);
            }
            catch (Exception)
            {
                MessageBox.Show("Lỗi gửi dữ liệu đến máy khách");
            }
        }

        private void sendCallback(IAsyncResult ar)
        {
            try
            {
                Socket socket = (Socket)ar.AsyncState;
                socket.EndSend(ar);
                int clientIndex = getClientIndex(socket);

                BeginInvoke((Action)(() =>
                {
                    Label labelTime = createChatTime();
                    Panel panelMessage = createChatMessage(prevMessage, DockStyle.Right);
                    panelMessage.Tag = labelTime;

                    panelViews[clientIndex].Controls.Add(labelTime);
                    labelTime.BringToFront();
                    panelViews[clientIndex].Controls.Add(panelMessage);
                    panelMessage.BringToFront();
                    panelViews[clientIndex].ScrollControlIntoView(panelMessage);

                    txtMessage.Clear();
                    txtMessage.Focus();
                }));
            }
            catch (Exception)
            {
                MessageBox.Show("Lỗi kết thúc gửi dữ liệu");
            }
        }

        private void ptbImage_Click(object sender, EventArgs e)
        {
            if (clients.Count == 0)
            {
                MessageBox.Show("Chưa có người dùng để chat");
                return;
            }

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    byte[] dataType = Encoding.UTF8.GetBytes(SEND_IMAGE);

                    Bitmap bmp = new Bitmap(openFileDialog.FileName);
                    MemoryStream ms = new MemoryStream();

                    ImageFormat format = openFileDialog.FileName.EndsWith(".jpg")
                        || openFileDialog.FileName.EndsWith(".jpeg")  ?
                        ImageFormat.Jpeg : ImageFormat.Png;

                    bmp.Save(ms, format);
                    byte[] imageData = ms.ToArray();

                    byte[] mergedArray = dataType.Concat(imageData).ToArray();

                    int numberOfRecipients = 0;
                    for (int i = 0; i < pnlSidebar.Controls.Count; i++)
                    {
                        if ((pnlSidebar.Controls[i] as CheckBox).Checked)
                        {
                            clients[i].Send(mergedArray);
                            ImageConverter convertData = new ImageConverter();
                            Image image = (Image)convertData.ConvertFrom(imageData);

                            Label labelTime = createChatTime();
                            Panel panelImage = createChatImage(image, DockStyle.Right);
                            panelImage.Tag = labelTime;

                            panelViews[i].Controls.Add(labelTime);
                            labelTime.BringToFront();
                            panelViews[i].Controls.Add(panelImage);
                            panelImage.BringToFront();
                            panelViews[i].ScrollControlIntoView(panelImage);
                            numberOfRecipients++;
                        }
                    }

                    if (numberOfRecipients == 0)
                    {
                        MessageBox.Show("Vui lòng chọn người để gửi ảnh");
                    }

                    bmp.Dispose();
                    ms.Close();
                }
                catch (Exception)
                {
                    MessageBox.Show("Lỗi gửi hình ảnh hình ảnh phải dạng jpg , png ,...");
                }
            }
        }

        private void frm_Server_SizeChanged(object sender, EventArgs e)
        {
            pnlLeft.Visible = Width > 800;
        }

        private void ptbEmoji_Click(object sender, EventArgs e)
        {
            if (clients.Count == 0)
            {
                MessageBox.Show("Chưa có người dùng để chat");
                return;
            }
            pnlEmoji.Visible = !pnlEmoji.Visible;
        }

        private void ptbFile_Click(object sender, EventArgs e)
        {
            if (clients.Count == 0)
            {
                MessageBox.Show("Chưa có người dùng để chat.");
                return;
            }

            openFileDialog.Filter = "Supported Files|*.txt;*.doc;*.docx;*.pdf|All Files|*.*";
            openFileDialog.Title = "Chọn tệp để gửi";

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    sendFile(openFileDialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi gửi tệp: " + ex.Message);
                }
            }
        }

    }
}
