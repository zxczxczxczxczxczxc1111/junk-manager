# Разрядка букв и кнопки
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд. Новая задача
# заводит СВОЙ файл и не трогает чужие.
#
# Половина мутаций тут в арифметике разрядки, половина в разметке. Общего у них
# то, что ни одна не роняет сборку: неверный зазор это правильное число не в том
# месте, а стиль с ключом вместо неявного это валидный XAML, который просто
# никогда не применится.
#
# Метка `knopki:` в начале имени нужна для выборочного прогона:
#   powershell -ExecutionPolicy Bypass -File scripts\mutate.ps1 -Tolko 'knopki:'

@(
    @{ Imya = 'knopki: зазор ставится и после последней буквы, надпись уезжает с центра'
       Fayl = 'src/JunkManager.App/Controls/TrackedTextMetrics.cs'
       Iz   = 'return total + (fontSize * tracking * (glyphWidths.Count - 1));'
       V    = 'return total + (fontSize * tracking * glyphWidths.Count);' }

    @{ Imya = 'knopki: разрядка перестала зависеть от кегля'
       Fayl = 'src/JunkManager.App/Controls/TrackedTextMetrics.cs'
       Iz   = 'return total + (fontSize * tracking * (glyphWidths.Count - 1));'
       V    = 'return total + (tracking * (glyphWidths.Count - 1));' }

    @{ Imya = 'knopki: пустая строка перестала давать ноль'
       Fayl = 'src/JunkManager.App/Controls/TrackedTextMetrics.cs'
       Iz   = 'if (glyphWidths.Count == 0)'
       V    = 'if (false && glyphWidths.Count == 0)' }

    @{ Imya = 'knopki: кнопка безвозвратного удаления покрашена медью'
       Fayl = 'src/JunkManager.App/Theme/Controls.Buttons.xaml'
       Iz   = '<Setter Property="Background" Value="{StaticResource DangerFillBrush}"/>'
       V    = '<Setter Property="Background" Value="{StaticResource RiskBrush}"/>' }

    @{ Imya = 'knopki: базовый стиль забыл кольцо фокуса и вернулся к пунктиру'
       Fayl = 'src/JunkManager.App/Theme/Controls.Buttons.xaml'
       Iz   = @'
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="FocusVisualStyle" Value="{StaticResource FocusRingStyle}"/>
'@
       V    = @'
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="Focusable" Value="True"/>
'@ }

    @{ Imya = 'knopki: стиль ToggleButton стал именованным и перестал применяться сам'
       Fayl = 'src/JunkManager.App/Theme/Controls.Buttons.xaml'
       Iz   = '<Style TargetType="{x:Type ToggleButton}" BasedOn="{StaticResource ButtonBase}">'
       V    = '<Style x:Key="ToggleButtonStyle" TargetType="{x:Type ToggleButton}" BasedOn="{StaticResource ButtonBase}">' }

    @{ Imya = 'knopki: стиль RepeatButton стал именованным и перестал применяться сам'
       Fayl = 'src/JunkManager.App/Theme/Controls.Buttons.xaml'
       Iz   = '<Style TargetType="{x:Type RepeatButton}" BasedOn="{StaticResource ButtonBase}"/>'
       V    = '<Style x:Key="RepeatButtonStyle" TargetType="{x:Type RepeatButton}" BasedOn="{StaticResource ButtonBase}"/>' }

    @{ Imya = 'knopki: подпись связана TemplateBinding, конвертер типа не отработает'
       Fayl = 'src/JunkManager.App/Theme/Controls.Buttons.xaml'
       Iz   = 'Text="{Binding Content, RelativeSource={RelativeSource TemplatedParent}}"'
       V    = 'Text="{TemplateBinding Content}"' }

    @{ Imya = 'knopki: главная кнопка потеряла своё имя и с ней лиловую заливку'
       Fayl = 'src/JunkManager.App/Theme/Controls.Buttons.xaml'
       Iz   = '<Style x:Key="PrimaryButton" TargetType="{x:Type Button}"'
       V    = '<Style x:Key="PrimaryButtonV2" TargetType="{x:Type Button}"' }
)
