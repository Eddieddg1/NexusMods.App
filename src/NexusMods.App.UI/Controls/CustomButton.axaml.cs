using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using NexusMods.Icons;

namespace NexusMods.App.UI.Controls;

[PseudoClasses(":size-medium", ":size-small", ":fill-none", ":fill-strong", ":fill-weak", ":type-primary", ":type-secondary", ":type-tertiary")]
[TemplatePart("PART_LeftIcon",  typeof(UnifiedIcon))]
[TemplatePart("PART_RightIcon", typeof(UnifiedIcon))]
[TemplatePart("PART_Label", typeof(TextBlock))]
public class CustomButton : Button
{
    //protected override Type StyleKeyOverride { get; } = typeof(Button);

    public enum VisibleIcons { None, Left, Right, Both, }
    public enum Sizes { Medium, Small, }
    public enum Types { None, Primary, Secondary, Tertiary, }
    public enum Fills { None, Strong, Weak, }

    private UnifiedIcon? _leftIcon  = null;
    private UnifiedIcon? _rightIcon = null;
    private TextBlock? _label = null;
    private ContentPresenter? _content = null;
    private Border? _border = null;
    
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<CustomButton, string?>(nameof(Text), defaultValue: "Custom Button");
    public static readonly StyledProperty<IconValue?> LeftIconProperty = AvaloniaProperty.Register<CustomButton, IconValue?>(nameof(LeftIcon), defaultValue: IconValues.ChevronDown);
    public static readonly StyledProperty<IconValue?> RightIconProperty = AvaloniaProperty.Register<CustomButton, IconValue?>(nameof(RightIcon), defaultValue: IconValues.ChevronUp);
    
    public static readonly AttachedProperty<VisibleIcons> VisibleIconProperty = AvaloniaProperty.RegisterAttached<CustomButton, TemplatedControl, VisibleIcons>("VisibleIcon", defaultValue: VisibleIcons.None);
    public static readonly AttachedProperty<Types> TypeProperty = AvaloniaProperty.RegisterAttached<CustomButton, TemplatedControl, Types>("Type", defaultValue: Types.None);
    public static readonly AttachedProperty<Sizes> SizeProperty = AvaloniaProperty.RegisterAttached<CustomButton, TemplatedControl, Sizes>("Size", defaultValue: Sizes.Medium);
    public static readonly AttachedProperty<Fills> FillProperty = AvaloniaProperty.RegisterAttached<CustomButton, TemplatedControl, Fills>("Fill", defaultValue: Fills.None);
    public static readonly AttachedProperty<bool> ShowLabelProperty = AvaloniaProperty.RegisterAttached<CustomButton, TemplatedControl, bool>("ShowLabel", defaultValue: true);
    
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
    
    public VisibleIcons? VisibleIcon
    {
        get => GetValue(VisibleIconProperty);
        set => SetValue(VisibleIconProperty, value);
    }
    
    public IconValue? LeftIcon
    {
        get => GetValue(LeftIconProperty);
        set => SetValue(LeftIconProperty, value);
    }
    
    public IconValue? RightIcon
    {
        get => GetValue(RightIconProperty);
        set => SetValue(RightIconProperty, value);
    }
    
    public bool ShowLabel
    {
        get => GetValue(ShowLabelProperty);
        set => SetValue(ShowLabelProperty, value);
    }
    
    public Types Type
    {
        get => GetValue(TypeProperty);
        set => SetValue(TypeProperty, value);
    }
    
    public Sizes Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }
    
    public Fills Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnClick()
    {
        base.OnClick();
        
        Console.WriteLine("Button Clicked");
    }

    /// <inheritdoc/>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _leftIcon = e.NameScope.Find<UnifiedIcon>("PART_LeftIcon");
        _rightIcon = e.NameScope.Find<UnifiedIcon>("PART_RightIcon");
        _label = e.NameScope.Find<TextBlock>("PART_Label");
        _content = e.NameScope.Find<ContentPresenter>("PART_ContentPresenter");
        _border = e.NameScope.Find<Border>("PART_Border");

        if (_leftIcon == null || _rightIcon == null || _label == null || _content == null || _border == null) return;

        _leftIcon.Value = LeftIcon;
        _rightIcon.Value = RightIcon;

        _label.IsVisible = ShowLabel;

        // so we can use the traditional button as well as our own properties to set the button
        if (Content is null)
        {
            _content.IsVisible = false;
            _border.IsVisible = true;
        }
        else
        {
            _content.IsVisible = true;
            _border.IsVisible = false;
        }

        switch (VisibleIcon)
        {
            case VisibleIcons.None:
                _leftIcon!.IsVisible = false;
                _rightIcon!.IsVisible = false;
                break;
            case VisibleIcons.Left:
                _leftIcon!.IsVisible = true;
                _rightIcon!.IsVisible = false;
                break;
            case VisibleIcons.Right:
                _leftIcon!.IsVisible = false;
                _rightIcon!.IsVisible = true;
                break;
            case VisibleIcons.Both:
                _leftIcon!.IsVisible = true;
                _rightIcon!.IsVisible = true;
                break;
            default:
                _leftIcon!.IsVisible = false;
                _rightIcon!.IsVisible = false;
                break;
        }
        
        switch (Size)
        {
            case Sizes.Medium:
                PseudoClasses.Add(":size-medium");
                PseudoClasses.Remove(":size-small");
                break;
            case Sizes.Small:
                PseudoClasses.Remove(":size-medium");
                PseudoClasses.Add(":size-small");
                break;
        }
        
        switch (Fill)
        {
            case Fills.None:
                PseudoClasses.Add(":fill-none");
                PseudoClasses.Remove(":fill-strong");
                PseudoClasses.Remove(":fill-weak");
                break;
            case Fills.Strong:
                PseudoClasses.Remove(":fill-none");
                PseudoClasses.Add(":fill-strong");
                PseudoClasses.Remove(":fill-weak");
                break;
            case Fills.Weak:
                PseudoClasses.Remove(":fill-none");
                PseudoClasses.Remove(":fill-strong");
                PseudoClasses.Add(":fill-weak");
                break;
        }

        switch (Type) 
        {
            case Types.None:
                PseudoClasses.Remove(":type-primary");
                PseudoClasses.Remove(":type-secondary");
                PseudoClasses.Remove(":type-tertiary");
                break;
            case Types.Primary:
                PseudoClasses.Add(":type-primary");
                PseudoClasses.Remove(":type-secondary");
                PseudoClasses.Remove(":type-tertiary");
                break;
            case Types.Secondary:
                PseudoClasses.Remove(":type-primary");
                PseudoClasses.Add(":type-secondary");
                PseudoClasses.Remove(":type-tertiary");
                break;
            case Types.Tertiary:
                PseudoClasses.Remove(":type-primary");
                PseudoClasses.Remove(":type-secondary");
                PseudoClasses.Add(":type-tertiary");
                break;
        }
    }
}

