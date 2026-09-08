# Флажки, переключатели и поля ввода
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд. Новая задача
# заводит СВОЙ файл и не трогает чужие.
#
# Класс дефектов: свойства выделения у TextBox живут НЕ в шаблоне, поэтому
# полностью переписанный ControlTemplate их не трогает вовсе, и синее выделение
# Windows остаётся под своей рамкой. Это не видно на снимке пустого поля.
#
# Метка `polya:` в начале имени нужна для выборочного прогона:
#   powershell -ExecutionPolicy Bypass -File scripts\mutate.ps1 -Tolko 'polya:'

@(
    @{ Imya = 'polya: чернила выделения не перекрыты, текст под выделением системный'
       Fayl = 'src/JunkManager.App/Theme/Controls.Inputs.xaml'
       Iz   = '<Setter Property="SelectionTextBrush" Value="{StaticResource AccentInkBrush}"/>'
       V    = '<Setter Property="AcceptsReturn" Value="False"/>' }

    @{ Imya = 'polya: каретка не перекрыта и остаётся системной'
       Fayl = 'src/JunkManager.App/Theme/Controls.Inputs.xaml'
       Iz   = '<Setter Property="CaretBrush" Value="{StaticResource AccentBrush}"/>'
       V    = '<Setter Property="AcceptsTab" Value="False"/>' }

    @{ Imya = 'polya: контекстное меню поля осталось системным'
       Fayl = 'src/JunkManager.App/Theme/Controls.Inputs.xaml'
       Iz   = '<Setter Property="ContextMenu" Value="{DynamicResource EditContextMenu}"/>'
       V    = '<Setter Property="IsReadOnly" Value="False"/>' }

    @{ Imya = 'polya: выделение стало полупрозрачным и съело свои чернила'
       Fayl = 'src/JunkManager.App/Theme/Controls.Inputs.xaml'
       Iz   = '<Setter Property="SelectionOpacity" Value="1"/>'
       V    = '<Setter Property="SelectionOpacity" Value="0.4"/>' }

    @{ Imya = 'polya: вернулось подчёркивание клавиши доступа'
       Fayl = 'src/JunkManager.App/Theme/Controls.Inputs.xaml'
       Iz   = 'RecognizesAccessKey="False"'
       V    = 'RecognizesAccessKey="True"'
       Vse  = $true }

    @{ Imya = 'polya: длина штриха галочки взята из SVG в пикселях'
       Fayl = 'src/JunkManager.App/Theme/Controls.Inputs.xaml'
       Iz   = 'StrokeDashArray="7 7"'
       V    = 'StrokeDashArray="16 16"' }

    @{ Imya = 'polya: галочка нарисована сразу, смещение штриха обнулено'
       Fayl = 'src/JunkManager.App/Theme/Controls.Inputs.xaml'
       Iz   = 'StrokeDashOffset="7"'
       V    = 'StrokeDashOffset="0"' }

    @{ Imya = 'polya: стиль флажка стал именованным и перестал применяться сам'
       Fayl = 'src/JunkManager.App/Theme/Controls.Inputs.xaml'
       Iz   = '<Style TargetType="{x:Type CheckBox}">'
       V    = '<Style x:Key="CheckBoxStyle" TargetType="{x:Type CheckBox}">' }

    @{ Imya = 'polya: стиль переключателя стал именованным и перестал применяться сам'
       Fayl = 'src/JunkManager.App/Theme/Controls.Inputs.xaml'
       Iz   = '<Style TargetType="{x:Type RadioButton}">'
       V    = '<Style x:Key="RadioButtonStyle" TargetType="{x:Type RadioButton}">' }
)
