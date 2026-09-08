# Purge обработчиков очистки Windows
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.
#
# Отдельная оговорка про безопасность прогона. Мутация «предохранитель снят»
# на этой машине действительно снимает единственную преграду перед настоящим
# IEmptyVolumeCache::Purge, поэтому тест, который её ловит, работает с именем
# обработчика, которого в реестре нет. Иначе прогон мутаций чистил бы кэш
# хоста и был бы сам разрушительной операцией.

@(
    @{ Imya = 'Чёрный список перед Purge перестаёт работать вовсе'
       Fayl = 'src/JunkManager.Deletion/VolumeCachePurger.cs'
       Iz   = 'if (VolumeCacheCatalog.Blacklist.Contains(entry.KeyName.Trim()))'
       V    = 'if (false && VolumeCacheCatalog.Blacklist.Contains(entry.KeyName.Trim()))' }

    @{ Imya = 'Чёрный список перед Purge перестаёт нормализовать имя ключа'
       Fayl = 'src/JunkManager.Deletion/VolumeCachePurger.cs'
       Iz   = 'Blacklist.Contains(entry.KeyName.Trim())'
       V    = 'Blacklist.Contains(entry.KeyName)' }
)
