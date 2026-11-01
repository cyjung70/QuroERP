using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Quro.Grid.Data;
using Quro.UI;
using QuroErp.Contracts.Common.Codes;

namespace QuroErp.Web.Components.Screens;

/// <summary>
/// The record of an S form as the input wrappers see it: one row of a <see cref="QuroDataSet{T}"/> (qurogrid-integration.md 10).
/// A wrapper with a <c>Field</c> reads and writes the column here and draws the Quro.UI control for it.
/// </summary>
public abstract class QuroFormData
{
    /// <summary>Raised after a value of the record changes, so the form draws again.</summary>
    public event Action? Changed;

    public abstract object? GetValue(string field);

    public abstract void SetValue(string field, object? value);

    /// <summary>
    /// Marks the column required for <c>Validate</c>, on top of the DTO's <c>[Required]</c>, so the required marker and
    /// the save check agree (qurogrid-integration.md 10 "필수"). <paramref name="excluded"/> takes a field the policy hides out
    /// of the check, <c>[Required]</c> or not (field-policy.md 3); the server still checks it.
    /// </summary>
    public abstract void Require(string field, bool required, bool excluded = false);

    /// <summary>Changes every value set on the field before it is kept, e.g. letter case and empty text as null.</summary>
    public abstract void Normalize(string field, Func<object?, object?> normalize);

    public abstract RenderFragment TextBox(string field, QuroCharType charType, int? maxLength, bool password, bool locked);

    public abstract RenderFragment DatePicker(string field, IFormatProvider culture, bool locked);

    public abstract RenderFragment ComboBox(string field, QuroDataSet<CodeItemDto> codes, bool locked, IStringLocalizer localizer);

    protected void OnChanged() => Changed?.Invoke();
}

/// <summary>The one-row data set of a <see cref="QuroSingleForm{TModel}"/>.</summary>
public sealed class QuroFormData<TModel> : QuroFormData
    where TModel : class
{
    private readonly Dictionary<string, Func<object?, object?>> _normalizers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _modelRequired = new(StringComparer.Ordinal);

    public QuroFormData() => DataSet.ValueChanged += OnValueChanged;

    public QuroDataSet<TModel> DataSet { get; } = new();

    /// <summary>The record row; the form keeps exactly one.</summary>
    public int RowKey => DataSet.CurrentRowKey ?? throw new InvalidOperationException("The form has no record.");

    /// <summary>The record object. Wrappers bound with @bind-Value write it directly.</summary>
    public TModel Item => DataSet.GetItem(RowKey);

    /// <summary>
    /// A new record: a blank row, so it is not a change until a value differs from the new record's
    /// (HasChanges and Validate skip it until then).
    /// </summary>
    public void StartNew(TModel model)
    {
        DataSet.Load([]);
        DataSet.MoveTo(DataSet.AddBlankRow(model));
    }

    /// <summary>A loaded or saved record.</summary>
    public void SetLoaded(TModel model) => DataSet.Load([model]);

    public override object? GetValue(string field) => DataSet.GetValue(RowKey, field);

    public override void SetValue(string field, object? value) => DataSet.SetValue(RowKey, field, value);

    public override void Require(string field, bool required, bool excluded = false)
    {
        var column = DataSet.GetColumn(field);
        if (!_modelRequired.TryGetValue(column.Name, out var modelRequired))
        {
            modelRequired = column.Required;
            _modelRequired[column.Name] = modelRequired;
        }
        var now = !excluded && (modelRequired || required);
        if (column.Required != now)
        {
            DataSet.SetColumn(column with { Required = now });
        }
    }

    public override void Normalize(string field, Func<object?, object?> normalize) =>
        _normalizers[DataSet.GetColumn(field).Name] = normalize;

    public override RenderFragment TextBox(string field, QuroCharType charType, int? maxLength, bool password, bool locked) => builder =>
    {
        builder.OpenComponent<QuroTextBox<TModel>>(0);
        builder.AddComponentParameter(1, nameof(QuroTextBox<TModel>.DataSet), DataSet);
        builder.AddComponentParameter(2, nameof(QuroTextBox<TModel>.Field), field);
        builder.AddComponentParameter(3, nameof(QuroTextBox<TModel>.CharType), charType);
        builder.AddComponentParameter(4, nameof(QuroTextBox<TModel>.MaxLength), maxLength);
        builder.AddComponentParameter(5, nameof(QuroTextBox<TModel>.Password), password);
        builder.AddComponentParameter(6, nameof(QuroTextBox<TModel>.ReadOnly), locked);
        builder.CloseComponent();
    };

    public override RenderFragment DatePicker(string field, IFormatProvider culture, bool locked) => builder =>
    {
        builder.OpenComponent<QuroDatePicker<TModel>>(0);
        builder.AddComponentParameter(1, nameof(QuroDatePicker<TModel>.DataSet), DataSet);
        builder.AddComponentParameter(2, nameof(QuroDatePicker<TModel>.Field), field);
        builder.AddComponentParameter(3, nameof(QuroDatePicker<TModel>.Culture), culture);
        builder.AddComponentParameter(4, nameof(QuroDatePicker<TModel>.ReadOnly), locked);
        builder.CloseComponent();
    };

    public override RenderFragment ComboBox(string field, QuroDataSet<CodeItemDto> codes, bool locked, IStringLocalizer localizer) => builder =>
    {
        builder.OpenComponent<QuroComboBox<TModel, CodeItemDto>>(0);
        builder.AddComponentParameter(1, nameof(QuroComboBox<TModel, CodeItemDto>.DataSet), DataSet);
        builder.AddComponentParameter(2, nameof(QuroComboBox<TModel, CodeItemDto>.Field), field);
        builder.AddComponentParameter(3, nameof(QuroComboBox<TModel, CodeItemDto>.OptionsSource), codes);
        builder.AddComponentParameter(4, nameof(QuroComboBox<TModel, CodeItemDto>.ValueField), nameof(CodeItemDto.Code));
        builder.AddComponentParameter(5, nameof(QuroComboBox<TModel, CodeItemDto>.TextField), nameof(CodeItemDto.Name));
        // The empty first entry clears an optional value, as the plain select did
        builder.AddComponentParameter(6, nameof(QuroComboBox<TModel, CodeItemDto>.EmptyKey), "Common.Select");
        builder.AddComponentParameter(7, nameof(QuroComboBox<TModel, CodeItemDto>.Localizer), localizer);
        builder.AddComponentParameter(8, nameof(QuroComboBox<TModel, CodeItemDto>.ReadOnly), locked);
        builder.CloseComponent();
    };

    private void OnValueChanged(object? sender, DataSetValueChangedEventArgs e)
    {
        if (_normalizers.TryGetValue(e.Field, out var normalize) && normalize(e.NewValue) is var kept && !Equals(kept, e.NewValue))
        {
            // Raises ValueChanged again with the kept value, which then passes unchanged
            DataSet.SetValue(e.RowKey, e.Field, kept);
            return;
        }
        OnChanged();
    }
}
