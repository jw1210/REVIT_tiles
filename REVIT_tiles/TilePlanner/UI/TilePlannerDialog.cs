using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TilePlanner.Core;

namespace TilePlanner.UI
{
    /// <summary>
    /// 磁磚計畫設定對話框（純 C# 建構，不需 XAML 編譯）
    /// </summary>
    public class TilePlannerDialog : Window
    {
        // 磁磚設定
        private TextBox txtTileWidth;
        private TextBox txtTileHeight;

        // 灰縫設定
        private TextBox txtGroutWidth;

        // 排列模式
        private RadioButton rbGrid;
        private RadioButton rbRunningBond;
        private StackPanel pnlOffset;
        private TextBox txtOffsetInput;

        // 快速選擇
        private ComboBox cmbPreset;

        public TilePlannerDialog()
        {
            BuildUI();
        }

        public TileConfig GetTileConfig()
        {
            double offsetPercent = ParseDouble(txtOffsetInput.Text, 50);
            // 限制在 1~99 之間
            offsetPercent = Math.Max(1, Math.Min(99, offsetPercent));

            return new TileConfig
            {
                TileWidth = ParseDouble(txtTileWidth.Text, 200),
                TileHeight = ParseDouble(txtTileHeight.Text, 200),
                TileThickness = 0,
                GroutWidth = ParseDouble(txtGroutWidth?.Text, 3),
                GroutThickness = 0, // Since gap in PartUtils only needs width, thickness is irrelevant.
                PatternType = rbRunningBond.IsChecked == true
                    ? TilePatternType.RunningBond
                    : TilePatternType.Grid,
                RunningBondOffset = offsetPercent / 100.0
            };
        }

        private void BuildUI()
        {
            Title = "磁磚計畫設定";
            Width = 440;
            Height = 460;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 標題
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 快速選擇
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 磁磚設定

            // 隱藏灰縫設定列
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) }); 

            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 排列模式
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 按鈕

            // === Row 0: 標題 ===
            var titleBlock = new TextBlock
            {
                Text = "磁磚計畫設定",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(12, 8, 12, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(51, 51, 51))
            };
            Grid.SetRow(titleBlock, 0);
            mainGrid.Children.Add(titleBlock);

            // === Row 1: 快速選擇 ===
            var presetGroup = CreateGroupBox("快速選擇", 1);
            var presetPanel = new StackPanel { Orientation = Orientation.Horizontal };
            presetPanel.Children.Add(new Label { Content = "預設規格：", VerticalAlignment = VerticalAlignment.Center });
            cmbPreset = new ComboBox { Width = 200, Height = 24 };
            cmbPreset.Items.Add("自訂");
            cmbPreset.Items.Add("20 × 20 cm");
            cmbPreset.Items.Add("30 × 30 cm");
            cmbPreset.Items.Add("30 × 60 cm");
            cmbPreset.Items.Add("60 × 60 cm");
            cmbPreset.Items.Add("45 × 90 cm");
            cmbPreset.Items.Add("60 × 120 cm");
            cmbPreset.Items.Add("二丁掛 (60 × 227 mm)");
            cmbPreset.SelectedIndex = 0;
            cmbPreset.SelectionChanged += CmbPreset_SelectionChanged;
            presetPanel.Children.Add(cmbPreset);
            presetGroup.Content = presetPanel;
            mainGrid.Children.Add(presetGroup);

            // === Row 2: 磁磚設定 ===
            var tileGroup = CreateGroupBox("磁磚設定", 2);
            var tileGrid = CreateInputGrid();
            txtTileWidth = AddInputRow(tileGrid, 0, "寬度：", "200", "mm");
            txtTileHeight = AddInputRow(tileGrid, 1, "高度：", "200", "mm");
            tileGroup.Content = tileGrid;
            mainGrid.Children.Add(tileGroup);

            // === Row 3: 灰縫設定 ===
            var groutGroup = CreateGroupBox("灰縫設定（分割間隙）", 3);
            var groutGrid = CreateInputGrid();
            txtGroutWidth = AddInputRow(groutGrid, 0, "寬度：", "3", "mm");
            groutGroup.Content = groutGrid;
            mainGrid.Children.Add(groutGroup);

            // === Row 4: 排列模式 ===
            var patternGroup = CreateGroupBox("排列模式", 4);
            var patternPanel = new StackPanel();

            rbGrid = new RadioButton
            {
                Content = "正排（對齊排列）",
                IsChecked = true,
                Margin = new Thickness(0, 4, 0, 4),
                FontSize = 13
            };
            rbGrid.Checked += RbPattern_Checked;
            patternPanel.Children.Add(rbGrid);

            rbRunningBond = new RadioButton
            {
                Content = "交丁排（偏移排列）",
                Margin = new Thickness(0, 4, 0, 4),
                FontSize = 13
            };
            rbRunningBond.Checked += RbPattern_Checked;
            patternPanel.Children.Add(rbRunningBond);

            // 交丁偏移設定面板
            pnlOffset = new StackPanel
            {
                Margin = new Thickness(20, 8, 0, 0),
                Visibility = Visibility.Collapsed  // 預設隱藏，選交丁排才顯示
            };

            // 偏移百分比輸入
            var offsetInputPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            offsetInputPanel.Children.Add(new TextBlock
            {
                Text = "偏移百分比：",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13,
                Margin = new Thickness(0, 0, 4, 0)
            });

            txtOffsetInput = new TextBox
            {
                Width = 60,
                Height = 26,
                Text = "50",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            offsetInputPanel.Children.Add(txtOffsetInput);

            offsetInputPanel.Children.Add(new TextBlock
            {
                Text = "% （範圍 1~99）",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Margin = new Thickness(4, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120))
            });
            pnlOffset.Children.Add(offsetInputPanel);

            // 常用百分比快捷按鈕
            var presetOffsetPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 0)
            };
            presetOffsetPanel.Children.Add(new TextBlock
            {
                Text = "常用：",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                FontSize = 12
            });

            // 二丁掛表示方式：「37分」= 比例 3:7 = 偏移 30%
            var presets = new[]
            {
                ("37分 30%", 30),
                ("46分 40%", 40),
                ("55分 50%", 50),
                ("64分 60%", 60),
                ("73分 70%", 70)
            };

            foreach (var (label, value) in presets)
            {
                var btn = new Button
                {
                    Content = label,
                    MinWidth = 55,
                    Height = 28,
                    Margin = new Thickness(2),
                    Tag = value,
                    FontSize = 10,
                    Padding = new Thickness(4, 2, 4, 2)
                };
                btn.Click += BtnPresetOffset_Click;
                presetOffsetPanel.Children.Add(btn);
            }
            pnlOffset.Children.Add(presetOffsetPanel);
            patternPanel.Children.Add(pnlOffset);

            patternGroup.Content = patternPanel;
            mainGrid.Children.Add(patternGroup);

            // === Row 5: 按鈕列 ===
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8)
            };

            var btnOK = new Button
            {
                Content = "確定",
                Width = 80,
                Height = 30,
                Margin = new Thickness(4),
                IsDefault = true,
                Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            };
            btnOK.Click += BtnOK_Click;
            buttonPanel.Children.Add(btnOK);

            var btnCancel = new Button
            {
                Content = "取消",
                Width = 80,
                Height = 30,
                Margin = new Thickness(4),
                IsCancel = true
            };
            btnCancel.Click += BtnCancel_Click;
            buttonPanel.Children.Add(btnCancel);

            Grid.SetRow(buttonPanel, 5);
            mainGrid.Children.Add(buttonPanel);

            Content = mainGrid;
        }

        // ===== UI 輔助 =====

        private GroupBox CreateGroupBox(string header, int row)
        {
            var group = new GroupBox
            {
                Header = header,
                Margin = new Thickness(8, 4, 8, 4),
                Padding = new Thickness(8),
                FontSize = 13
            };
            Grid.SetRow(group, row);
            return group;
        }

        private Grid CreateInputGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            return grid;
        }

        private TextBox AddInputRow(Grid grid, int row, string label, string defaultValue, string unit)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var lbl = new Label
            {
                Content = label,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            grid.Children.Add(lbl);

            var textBox = new TextBox
            {
                Text = defaultValue,
                Height = 24,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2)
            };
            Grid.SetRow(textBox, row);
            Grid.SetColumn(textBox, 1);
            grid.Children.Add(textBox);

            var unitLabel = new Label
            {
                Content = unit,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12
            };
            Grid.SetRow(unitLabel, row);
            Grid.SetColumn(unitLabel, 2);
            grid.Children.Add(unitLabel);

            return textBox;
        }

        // ===== 事件處理 =====

        private void CmbPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbPreset.SelectedIndex <= 0) return;

            switch (cmbPreset.SelectedIndex)
            {
                case 1: SetTileDimensions(200, 200, 10); break;
                case 2: SetTileDimensions(300, 300, 10); break;
                case 3: SetTileDimensions(300, 600, 10); break;
                case 4: SetTileDimensions(600, 600, 10); break;
                case 5: SetTileDimensions(450, 900, 10); break;
                case 6: SetTileDimensions(600, 1200, 10); break;
                case 7: SetTileDimensions(60, 227, 10); break;
            }
        }

        private void SetTileDimensions(double width, double height, double thickness)
        {
            if (txtTileWidth != null) txtTileWidth.Text = width.ToString();
            if (txtTileHeight != null) txtTileHeight.Text = height.ToString();
            // if (txtTileThickness != null) txtTileThickness.Text = thickness.ToString();
        }

        private void RbPattern_Checked(object sender, RoutedEventArgs e)
        {
            if (pnlOffset == null) return;
            bool isRunningBond = rbRunningBond.IsChecked == true;
            // 使用 Visibility 而非 IsEnabled/Opacity，確保完全顯示/隱藏
            pnlOffset.Visibility = isRunningBond ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnPresetOffset_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn?.Tag != null && int.TryParse(btn.Tag.ToString(), out int value))
            {
                txtOffsetInput.Text = value.ToString();
            }
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInputs()) return;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ===== 驗證 =====

        private bool ValidateInputs()
        {
            if (!ValidateField(txtTileWidth, "磁磚寬度")) return false;
            if (!ValidateField(txtTileHeight, "磁磚高度")) return false;
            if (!ValidateField(txtGroutWidth, "灰縫寬度")) return false;

            if (rbRunningBond.IsChecked == true)
            {
                if (!double.TryParse(txtOffsetInput.Text, out double offset) ||
                    offset < 1 || offset > 99)
                {
                    MessageBox.Show("請輸入有效的交丁偏移百分比（1~99）。", "輸入錯誤",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtOffsetInput.Focus();
                    txtOffsetInput.SelectAll();
                    return false;
                }
            }

            return true;
        }

        private bool ValidateField(TextBox textBox, string fieldName)
        {
            if (textBox == null) return true; // 若該欄位不存在則直接略過驗證
            if (!double.TryParse(textBox.Text, out double value) || value <= 0)
            {
                MessageBox.Show($"請輸入有效的{fieldName}（正數）。", "輸入錯誤",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                textBox.Focus();
                textBox.SelectAll();
                return false;
            }
            return true;
        }

        private double ParseDouble(string text, double defaultValue)
        {
            if (double.TryParse(text, out double result) && result > 0)
                return result;
            return defaultValue;
        }
    }
}
