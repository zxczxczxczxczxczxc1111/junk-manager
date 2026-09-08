# Свой выпадающий список
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд. Новая задача
# заводит СВОЙ файл и не трогает чужие.
#
# Поводом ко всему требованию «ни одного стандартного контрола» был именно
# штатный выпадающий список со своим синим выделением. Мутации бьют по тем
# местам, где системный вид возвращается тихо: анимация всплытия, подсветка
# строки, отказ от вида по умолчанию.
#
# Метка `spisok:` в начале имени нужна для выборочного прогона:
#   powershell -ExecutionPolicy Bypass -File scripts\mutate.ps1 -Tolko 'spisok:'

@(
    @{ Imya = 'spisok: всплытие анимируется системой'
       Fayl = 'src/JunkManager.App/Theme/Controls.Combo.xaml'
       Iz   = 'PopupAnimation="None"'
       V    = 'PopupAnimation="Fade"' }

    @{ Imya = 'spisok: список стал редактируемым и притащил системное поле ввода'
       Fayl = 'src/JunkManager.App/Theme/Controls.Combo.xaml'
       Iz   = '<Setter Property="IsEditable" Value="False"/>'
       V    = '<Setter Property="IsEditable" Value="True"/>' }

    @{ Imya = 'spisok: стиль строки стал именованным и перестал применяться сам'
       Fayl = 'src/JunkManager.App/Theme/Controls.Combo.xaml'
       Iz   = '<Style TargetType="{x:Type ComboBoxItem}">'
       V    = '<Style x:Key="ComboBoxItemStyle" TargetType="{x:Type ComboBoxItem}">' }

    @{ Imya = 'spisok: клавиатурная подсветка строки осталась системной'
       Fayl = 'src/JunkManager.App/Theme/Controls.Combo.xaml'
       Iz   = '<Trigger Property="IsHighlighted" Value="True">'
       V    = '<Trigger Property="IsEnabled" Value="True">' }

    @{ Imya = 'spisok: выбранная строка перестала показывать галочку'
       Fayl = 'src/JunkManager.App/Theme/Controls.Combo.xaml'
       Iz   = '<Trigger Property="IsSelected" Value="True">'
       V    = '<Trigger Property="IsFocused" Value="True">' }

    @{ Imya = 'spisok: закрытая коробка взяла кнопочный стиль вместо своего'
       Fayl = 'src/JunkManager.App/Theme/Controls.Combo.xaml'
       Iz   = 'Style="{StaticResource ComboToggleStyle}"'
       V    = 'Style="{StaticResource ButtonBase}"' }

    @{ Imya = 'spisok: закрытая коробка не отказалась от вида по умолчанию'
       Fayl = 'src/JunkManager.App/Theme/Controls.Combo.xaml'
       Iz   = @'
  <Style x:Key="ComboToggleStyle" TargetType="{x:Type ToggleButton}">
    <Setter Property="OverridesDefaultStyle" Value="True"/>
'@
       V    = @'
  <Style x:Key="ComboToggleStyle" TargetType="{x:Type ToggleButton}">
    <Setter Property="OverridesDefaultStyle" Value="False"/>
'@ }
)
