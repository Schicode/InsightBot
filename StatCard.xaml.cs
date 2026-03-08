using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace InsightBot;

public sealed partial class StatCard : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatCard),
            new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(object), typeof(StatCard),
            new PropertyMetadata(null, OnValueChanged));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public StatCard() => InitializeComponent();

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((StatCard)d).LabelText.Text = e.NewValue as string ?? string.Empty;

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((StatCard)d).ValueText.Text = e.NewValue?.ToString() ?? "0";
}
