# Обработчики очистки Windows
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'Чёрный список обработчиков перестаёт исключать DownloadsFolder'
       Fayl = 'src/JunkManager.Core/Sources/VolumeCache/VolumeCacheCatalog.cs'
       Iz   = '[.. raw.Where(e => !Blacklist.Contains(e.KeyName.Trim()))]'
       V    = '[.. raw]' }

    @{ Imya = 'Чёрный список начинает работать по CLSID вместо имени ключа'
       Fayl = 'src/JunkManager.Core/Sources/VolumeCache/VolumeCacheCatalog.cs'
       Iz   = '!Blacklist.Contains(e.KeyName.Trim())'
       V    = 'e.Clsid != new Guid("{C0E13E61-0CC6-11d1-BBB6-0060978B2AE6}")' }
    @{ Imya = 'Том обработчиков снова вбивается буквой C'
       Fayl = 'src/JunkManager.Core/Sources/VolumeCache/VolumeCacheCatalog.cs'
       Iz   = 'Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))'
       V    = '"Z:" + Path.DirectorySeparatorChar' }
)
