//Views/AdvancedDataGridControl.xaml.cs - KOMPLETNÁ FUNKČNÁ VERZIA
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using RpaWinUiComponents.AdvancedWinUiDataGrid.Events;
using RpaWinUiComponents.AdvancedWinUiDataGrid.Models;
using RpaWinUiComponents.AdvancedWinUiDataGrid.ViewModels;
using RpaWinUiComponents.AdvancedWinUiDataGrid.Configuration;

namespace RpaWinUiComponents.AdvancedWinUiDataGrid.Views
{
    /// <summary>
    /// Hlavný UserControl pre AdvancedWinUiDataGrid komponent
    /// </summary>
    public sealed partial class AdvancedDataGridControl : UserControl, IDisposable
    {
        private AdvancedDataGridViewModel? _viewModel;
        private readonly ILogger<AdvancedDataGridControl> _logger;
        private bool _disposed = false;
        private bool _isKeyboardShortcutsVisible = false;

        // Grid generation state
        private readonly Dictionary<int, Grid> _rowGrids = new();
        private readonly List<ColumnDefinition> _currentColumns = new();
        private readonly Dictionary<string, Border> _headerBorders = new();

        public AdvancedDataGridControl()
        {
            InitializeComponent();

            var loggerProvider = GetLoggerProvider();
            _logger = loggerProvider.CreateLogger<AdvancedDataGridControl>();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            _logger.LogDebug("AdvancedDataGridControl created");
        }

        #region Properties and Events

        public AdvancedDataGridViewModel? ViewModel
        {
            get => _viewModel;
            private set
            {
                if (_viewModel != null)
                {
                    _viewModel.ErrorOccurred -= OnViewModelError;
                    _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                }

                _viewModel = value;

                if (_viewModel != null)
                {
                    _viewModel.ErrorOccurred += OnViewModelError;
                    _viewModel.PropertyChanged += OnViewModelPropertyChanged;

                    // Sync keyboard shortcuts visibility
                    _isKeyboardShortcutsVisible = _viewModel.IsKeyboardShortcutsVisible;
                    UpdateKeyboardShortcutsVisibility();
                }

                DataContext = _viewModel;
            }
        }

        public event EventHandler<ComponentErrorEventArgs>? ErrorOccurred;

        #endregion

        #region Public API Methods

        /// <summary>
        /// Inicializuje komponent s konfiguráciou stĺpcov a validáciami
        /// </summary>
        public async Task InitializeAsync(
            List<ColumnDefinition> columns,
            List<ValidationRule>? validationRules = null,
            ThrottlingConfig? throttling = null,
            int initialRowCount = 100)
        {
            ThrowIfDisposed();

            try
            {
                _logger.LogInformation("Initializing AdvancedDataGrid with {ColumnCount} columns, {InitialRowCount} rows",
                    columns?.Count ?? 0, initialRowCount);

                if (_viewModel == null)
                {
                    _viewModel = CreateViewModel();
                    ViewModel = _viewModel;
                }

                await _viewModel.InitializeAsync(columns, validationRules ?? new List<ValidationRule>(), throttling, initialRowCount);

                // Store current columns for grid generation
                _currentColumns.Clear();
                _currentColumns.AddRange(_viewModel.Columns);

                GenerateGridStructure();
                SetupNavigationService();

                _logger.LogInformation("AdvancedDataGrid initialized successfully with {InitialRowCount} rows", initialRowCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing AdvancedDataGrid");
                OnErrorOccurred(new ComponentErrorEventArgs(ex, "InitializeAsync"));
                throw;
            }
        }

        /// <summary>
        /// Načíta dáta z DataTable s automatickou validáciou
        /// </summary>
        public async Task LoadDataAsync(DataTable dataTable)
        {
            ThrowIfDisposed();

            try
            {
                if (_viewModel == null)
                    throw new InvalidOperationException("Component must be initialized first! Call InitializeAsync() before LoadDataAsync().");

                if (!_viewModel.IsInitialized)
                    throw new InvalidOperationException("Component not properly initialized! Call InitializeAsync() with validation rules first.");

                _logger.LogInformation("Loading data from DataTable with {RowCount} rows", dataTable?.Rows.Count ?? 0);
                await _viewModel.LoadDataAsync(dataTable);
                _logger.LogInformation("Data loaded successfully with applied validations");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading data from DataTable");
                OnErrorOccurred(new ComponentErrorEventArgs(ex, "LoadDataAsync"));
                throw;
            }
        }

        /// <summary>
        /// Načíta dáta zo zoznamu dictionary objektov
        /// </summary>
        public async Task LoadDataAsync(List<Dictionary<string, object?>> data)
        {
            ThrowIfDisposed();

            try
            {
                if (_viewModel == null)
                    throw new InvalidOperationException("Component must be initialized first!");

                var dataTable = ConvertToDataTable(data);
                await _viewModel.LoadDataAsync(dataTable);
                _logger.LogInformation("Data loaded from dictionary list successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading data from dictionary list");
                OnErrorOccurred(new ComponentErrorEventArgs(ex, "LoadDataAsync"));
                throw;
            }
        }

        /// <summary>
        /// Exportuje validné dáta do DataTable
        /// </summary>
        public async Task<DataTable> ExportToDataTableAsync()
        {
            ThrowIfDisposed();

            try
            {
                if (_viewModel == null)
                    return new DataTable();

                var result = await _viewModel.ExportDataAsync();
                _logger.LogInformation("Data exported to DataTable with {RowCount} rows", result.Rows.Count);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting data");
                OnErrorOccurred(new ComponentErrorEventArgs(ex, "ExportToDataTableAsync"));
                return new DataTable();
            }
        }

        /// <summary>
        /// Validuje všetky riadky a vráti true ak sú všetky validné
        /// </summary>
        public async Task<bool> ValidateAllRowsAsync()
        {
            ThrowIfDisposed();

            try
            {
                if (_viewModel == null)
                    return false;

                var result = await _viewModel.ValidateAllRowsAsync();
                _logger.LogInformation("Validation completed, all valid: {AllValid}", result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating all rows");
                OnErrorOccurred(new ComponentErrorEventArgs(ex, "ValidateAllRowsAsync"));
                return false;
            }
        }

        /// <summary>
        /// Vymaže všetky dáta zo všetkých buniek
        /// </summary>
        public async Task ClearAllDataAsync()
        {
            ThrowIfDisposed();

            try
            {
                if (_viewModel?.ClearAllDataCommand?.CanExecute(null) == true)
                {
                    _viewModel.ClearAllDataCommand.Execute(null);
                    _logger.LogInformation("All data cleared");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing all data");
                OnErrorOccurred(new ComponentErrorEventArgs(ex, "ClearAllDataAsync"));
            }
        }

        /// <summary>
        /// Odstráni všetky prázdne riadky
        /// </summary>
        public async Task RemoveEmptyRowsAsync()
        {
            ThrowIfDisposed();

            try
            {
                if (_viewModel?.RemoveEmptyRowsCommand?.CanExecute(null) == true)
                {
                    _viewModel.RemoveEmptyRowsCommand.Execute(null);
                    _logger.LogInformation("Empty rows removed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing empty rows");
                OnErrorOccurred(new ComponentErrorEventArgs(ex, "RemoveEmptyRowsAsync"));
            }
        }

        /// <summary>
        /// Reset komponentu do pôvodného stavu
        /// </summary>
        public void Reset()
        {
            if (_disposed) return;

            try
            {
                _logger.LogInformation("Resetting AdvancedDataGrid");
                _viewModel?.Reset();
                ClearGridStructure();

                _isKeyboardShortcutsVisible = false;
                UpdateKeyboardShortcutsVisibility();

                _logger.LogInformation("AdvancedDataGrid reset completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting AdvancedDataGrid");
                OnErrorOccurred(new ComponentErrorEventArgs(ex, "Reset"));
            }
        }

        #endregion

        #region Event Handlers

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_disposed) return;

            try
            {
                _viewModel ??= CreateViewModel();
                ViewModel = _viewModel;
                SetupEventHandlers();

                UpdateKeyboardShortcutsVisibility();

                _logger.LogDebug("AdvancedDataGrid loaded");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during OnLoaded");
                OnErrorOccurred(new ComponentErrorEventArgs(ex, "OnLoaded"));
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_disposed) return;

            try
            {
                UnsubscribeAllEvents();
                _logger.LogDebug("AdvancedDataGrid unloaded");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during OnUnloaded");
            }
        }

        private void OnToggleKeyboardShortcuts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.LogDebug("Toggle keyboard shortcuts button clicked");

                _isKeyboardShortcutsVisible = !_isKeyboardShortcutsVisible;
                UpdateKeyboardShortcutsVisibility();

                if (_viewModel != null)
                {
                    _viewModel.IsKeyboardShortcutsVisible = _isKeyboardShortcutsVisible;
                }

                _logger.LogInformation("Keyboard shortcuts visibility toggled to: {IsVisible}", _isKeyboardShortcutsVisible);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling keyboard shortcuts");
                OnErrorOccurred(new ComponentErrorEventArgs(ex, "OnToggleKeyboardShortcuts_Click"));
            }
        }

        private void OnDeleteRowClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.CommandParameter is DataGridRow row)
                {
                    _viewModel?.DeleteRowCommand?.Execute(row);
                    _logger.LogDebug("Delete row button clicked for row: {RowIndex}", row.RowIndex);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling delete row click");
                OnErrorOccurred(new ComponentErrorEventArgs(ex, "OnDeleteRowClick"));
            }
        }

        private void OnCellEditingLostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is TextBox textBox && textBox.DataContext is DataGridCell cell)
                {
                    cell.IsEditing = false;
                    _logger.LogTrace("Cell editing ended for: {ColumnName}", cell.ColumnName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling cell editing lost focus");
            }
        }

        private void OnCellEditingKeyDown(object sender, KeyRoutedEventArgs e)
        {
            try
            {
                if (sender is TextBox textBox && textBox.DataContext is DataGridCell cell)
                {
                    switch (e.Key)
                    {
                        case VirtualKey.Enter:
                            if (!e.KeyStatus.IsMenuKeyDown) // Shift+Enter pre nový riadok
                            {
                                cell.IsEditing = false;
                                _viewModel?.NavigationService.MoveToNextRow();
                                e.Handled = true;
                            }
                            break;
                        case VirtualKey.Escape:
                            cell.CancelEditing();
                            cell.IsEditing = false;
                            e.Handled = true;
                            break;
                        case VirtualKey.Tab:
                            cell.IsEditing = false;
                            if (e.KeyStatus.IsMenuKeyDown) // Shift+Tab
                                _viewModel?.NavigationService.MoveToPreviousCell();
                            else
                                _viewModel?.NavigationService.MoveToNextCell();
                            e.Handled = true;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling cell editing key down");
            }
        }

        private void OnMainControlKeyDown(object sender, KeyRoutedEventArgs e)
        {
            try
            {
                var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

                if (ctrlPressed)
                {
                    switch (e.Key)
                    {
                        case VirtualKey.C:
                            if (_viewModel?.CopyCommand?.CanExecute(null) == true)
                            {
                                _viewModel.CopyCommand.Execute(null);
                                e.Handled = true;
                            }
                            break;
                        case VirtualKey.V:
                            if (_viewModel?.PasteCommand?.CanExecute(null) == true)
                            {
                                _viewModel.PasteCommand.Execute(null);
                                e.Handled = true;
                            }
                            break;
                    }
                }
                else
                {
                    switch (e.Key)
                    {
                        case VirtualKey.Tab:
                            if (e.KeyStatus.IsMenuKeyDown) // Shift+Tab
                                _viewModel?.NavigationService.MoveToPreviousCell();
                            else
                                _viewModel?.NavigationService.MoveToNextCell();
                            e.Handled = true;
                            break;
                        case VirtualKey.Enter:
                            _viewModel?.NavigationService.MoveToNextRow();
                            e.Handled = true;
                            break;
                        case VirtualKey.F2:
                            // Start editing current cell
                            var currentCell = _viewModel?.NavigationService.CurrentCell;
                            if (currentCell != null && !currentCell.IsReadOnly)
                            {
                                currentCell.IsEditing = true;
                                e.Handled = true;
                            }
                            break;
                        case VirtualKey.Delete:
                            // Clear current cell
                            var cellToDelete = _viewModel?.NavigationService.CurrentCell;
                            if (cellToDelete != null && !cellToDelete.IsReadOnly)
                            {
                                cellToDelete.Value = null;
                                e.Handled = true;
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling main control key down");
            }
        }

        private void OnViewModelError(object? sender, ComponentErrorEventArgs e)
        {
            OnErrorOccurred(e);
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AdvancedDataGridViewModel.Rows))
            {
                // Regenerate grid when rows change
                DispatcherQueue.TryEnqueue(() => GenerateGridStructure());
            }
            else if (e.PropertyName == nameof(AdvancedDataGridViewModel.IsKeyboardShortcutsVisible))
            {
                // Sync keyboard shortcuts visibility
                _isKeyboardShortcutsVisible = _viewModel?.IsKeyboardShortcutsVisible ?? false;
                UpdateKeyboardShortcutsVisibility();
            }
        }

        #endregion

        #region Grid Generation

        private void GenerateGridStructure()
        {
            try
            {
                if (_viewModel == null || _currentColumns.Count == 0)
                    return;

                GenerateHeaders();
                GenerateRowGrids();

                _logger.LogDebug("Generated grid structure with {ColumnCount} columns and {RowCount} rows",
                    _currentColumns.Count, _viewModel.Rows.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating grid structure");
            }
        }

        private void GenerateHeaders()
        {
            try
            {
                var headerGrid = HeaderGrid;
                headerGrid.Children.Clear();
                headerGrid.ColumnDefinitions.Clear();
                _headerBorders.Clear();

                double totalWidth = 0;

                // Create column definitions and headers
                for (int i = 0; i < _currentColumns.Count; i++)
                {
                    var column = _currentColumns[i];

                    // Add column definition
                    var colDef = new Microsoft.UI.Xaml.Controls.ColumnDefinition
                    {
                        Width = new GridLength(column.Width)
                    };
                    headerGrid.ColumnDefinitions.Add(colDef);
                    totalWidth += column.Width;

                    // Create header border
                    var headerBorder = new Border
                    {
                        Background = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
                        BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                        BorderThickness = new Thickness(0, 0, 1, 1),
                        Padding = new Thickness(8, 4)
                    };

                    var headerText = new TextBlock
                    {
                        Text = column.Header ?? column.Name,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };

                    headerBorder.Child = headerText;
                    Grid.SetColumn(headerBorder, i);
                    headerGrid.Children.Add(headerBorder);

                    _headerBorders[column.Name] = headerBorder;

                    // Add sorting click handler if allowed
                    if (column.AllowSort)
                    {
                        headerBorder.Tapped += (s, e) => OnHeaderTapped(column.Name);
                        headerBorder.PointerEntered += (s, e) => headerBorder.Background = new SolidColorBrush(Microsoft.UI.Colors.LightBlue);
                        headerBorder.PointerExited += (s, e) => headerBorder.Background = new SolidColorBrush(Microsoft.UI.Colors.LightGray);
                    }
                }

                // Set minimum width for the entire grid
                DataGridContainer.MinWidth = Math.Max(800, totalWidth);

                _logger.LogDebug("Generated {ColumnCount} headers with total width {TotalWidth}",
                    _currentColumns.Count, totalWidth);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating headers");
            }
        }

        private void GenerateRowGrids()
        {
            try
            {
                // Clear existing row grids
                _rowGrids.Clear();

                // We need to handle this differently since ItemsRepeater manages the row visuals
                // The actual row content generation happens in the ItemTemplate

                _logger.LogDebug("Row grids structure prepared for {RowCount} rows", _viewModel?.Rows.Count ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating row grids");
            }
        }

        private void OnHeaderTapped(string columnName)
        {
            try
            {
                // Implement sorting logic here
                _logger.LogDebug("Header tapped for column: {ColumnName}", columnName);

                // TODO: Implement sorting functionality
                // This would involve adding sorting logic to the ViewModel
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling header tap for column: {ColumnName}", columnName);
            }
        }

        private void ClearGridStructure()
        {
            try
            {
                HeaderGrid?.Children.Clear();
                HeaderGrid?.ColumnDefinitions.Clear();
                _headerBorders.Clear();
                _rowGrids.Clear();
                _currentColumns.Clear();

                _logger.LogDebug("Grid structure cleared");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing grid structure");
            }
        }

        #endregion

        #region Private Helper Methods

        private AdvancedDataGridViewModel CreateViewModel()
        {
            try
            {
                return DependencyInjectionConfig.GetService<AdvancedDataGridViewModel>()
                       ?? DependencyInjectionConfig.CreateViewModelWithoutDI();
            }
            catch
            {
                return DependencyInjectionConfig.CreateViewModelWithoutDI();
            }
        }

        private IDataGridLoggerProvider GetLoggerProvider()
        {
            try
            {
                return DependencyInjectionConfig.GetService<IDataGridLoggerProvider>()
                       ?? NullDataGridLoggerProvider.Instance;
            }
            catch
            {
                return NullDataGridLoggerProvider.Instance;
            }
        }

        private void SetupEventHandlers()
        {
            try
            {
                KeyDown += OnMainControlKeyDown;
                _logger.LogDebug("Event handlers set up");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up event handlers");
            }
        }

        private void SetupNavigationService()
        {
            try
            {
                if (_viewModel?.NavigationService != null)
                {
                    _viewModel.NavigationService.Initialize(_viewModel.Rows.ToList(), _viewModel.Columns.ToList());
                    _logger.LogDebug("Navigation service setup completed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up navigation service");
            }
        }

        private void UpdateKeyboardShortcutsVisibility()
        {
            try
            {
                if (KeyboardShortcutsPanel != null)
                {
                    KeyboardShortcutsPanel.Visibility = _isKeyboardShortcutsVisible ? Visibility.Visible : Visibility.Collapsed;
                }

                if (ToggleIcon != null)
                {
                    ToggleIcon.Text = _isKeyboardShortcutsVisible ? "▲" : "▼";
                }

                if (ToggleKeyboardShortcutsButton != null)
                {
                    var backgroundColor = _isKeyboardShortcutsVisible
                        ? new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue)
                        : new SolidColorBrush(Microsoft.UI.Colors.LightGray);

                    var foregroundColor = _isKeyboardShortcutsVisible
                        ? new SolidColorBrush(Microsoft.UI.Colors.White)
                        : new SolidColorBrush(Microsoft.UI.Colors.Black);

                    ToggleKeyboardShortcutsButton.Background = backgroundColor;
                    ToggleKeyboardShortcutsButton.Foreground = foregroundColor;
                }

                _logger.LogDebug("Keyboard shortcuts visibility updated: {IsVisible}", _isKeyboardShortcutsVisible);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating keyboard shortcuts visibility");
            }
        }

        private void UnsubscribeAllEvents()
        {
            try
            {
                KeyDown -= OnMainControlKeyDown;
                Loaded -= OnLoaded;
                Unloaded -= OnUnloaded;

                _logger.LogDebug("All events unsubscribed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unsubscribing events");
            }
        }

        private DataTable ConvertToDataTable(List<Dictionary<string, object?>> data)
        {
            var dataTable = new DataTable();

            if (data?.Count > 0)
            {
                // Create columns from first dictionary
                foreach (var key in data[0].Keys)
                {
                    dataTable.Columns.Add(key, typeof(object));
                }

                // Add rows
                foreach (var row in data)
                {
                    var dataRow = dataTable.NewRow();
                    foreach (var kvp in row)
                    {
                        dataRow[kvp.Key] = kvp.Value ?? DBNull.Value;
                    }
                    dataTable.Rows.Add(dataRow);
                }
            }

            return dataTable;
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                _logger?.LogDebug("Disposing AdvancedDataGridControl...");

                UnsubscribeAllEvents();

                if (_viewModel != null)
                {
                    _viewModel.ErrorOccurred -= OnViewModelError;
                    _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                    _viewModel.Dispose();
                    _viewModel = null;
                }

                ClearGridStructure();
                DataContext = null;

                _disposed = true;
                _logger?.LogInformation("AdvancedDataGridControl disposed successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during disposal");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AdvancedDataGridControl));
        }

        #endregion
    }
}