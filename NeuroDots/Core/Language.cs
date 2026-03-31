using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace NeuroCivilization.Core
{
    /// <summary>
    /// Система протоязыка — слова возникают и эволюционируют,
    /// значения закрепляются через социальное взаимодействие
    /// </summary>
    public class LanguageSystem
    {
        // Словарь: код слова → значение
        public Dictionary<int, WordMeaning> Dictionary = new();

        // Следующий свободный код
        int nextWordCode = 1;

        // Статистика
        public int TotalWordsCreated;
        public int ActiveWords => Dictionary.Count;
        public int ExtinctWords;

        static Random R = new();

        // ====== ЗНАЧЕНИЯ СЛОВ ======

        /// <summary>
        /// Создать новое слово для концепции
        /// </summary>
        public int CreateWord(Concept concept, int creatorId)
        {
            int code = nextWordCode++;
            Dictionary[code] = new WordMeaning
            {
                Code = code,
                PrimaryConcept = concept,
                CreatorId = creatorId,
                Strength = 0.3,
                Age = 0,
                Users = new HashSet<int> { creatorId }
            };
            TotalWordsCreated++;
            return code;
        }

        /// <summary>
        /// Человек использует слово рядом с другими → они учат его
        /// </summary>
        public void UseWord(int wordCode, int speakerId, List<int> listenerIds,
                            double speakerSkill)
        {
            if (!Dictionary.ContainsKey(wordCode)) return;

            var word = Dictionary[wordCode];
            word.UsageCount++;
            word.Strength = Math.Min(1, word.Strength + 0.01);
            word.Users.Add(speakerId);

            // Слушатели учат слово с вероятностью
            foreach (var id in listenerIds)
            {
                double learnChance = speakerSkill * 0.3;
                if (R.NextDouble() < learnChance)
                    word.Users.Add(id);
            }
        }

        /// <summary>
        /// Попытка коммуникации — говорящий пытается передать концепцию
        /// </summary>
        public CommunicationResult Communicate(Human speaker, Human listener,
            Concept concept)
        {
            if (speaker == null || listener == null) return new CommunicationResult();

            // Найти слово для концепции в словаре говорящего
            int wordCode = -1;
            foreach (var kvp in Dictionary)
            {
                if (kvp.Value.PrimaryConcept == concept &&
                    speaker.KnownWords.Contains(kvp.Key))
                {
                    wordCode = kvp.Key;
                    break;
                }
            }

            // Если слова нет — попытка создать новое
            if (wordCode == -1)
            {
                double createChance = speaker.Genes.EffCreativity *
                                      speaker.SkillSpeaking * 0.1;
                if (R.NextDouble() < createChance)
                {
                    wordCode = CreateWord(concept, speaker.Id);
                    speaker.KnownWords.Add(wordCode);
                    speaker.SkillSpeaking = Math.Min(1, speaker.SkillSpeaking + 0.005);
                }
                else
                {
                    return new CommunicationResult { Success = false, Gesture = true };
                }
            }

            // Передача слова
            bool understood = false;
            if (listener.KnownWords.Contains(wordCode))
            {
                // Слушатель знает это слово
                understood = true;
            }
            else
            {
                // Попытка понять по контексту
                double understandChance = listener.Genes.LearningSpeed *
                                          speaker.SkillSpeaking * 0.2;
                if (R.NextDouble() < understandChance)
                {
                    listener.KnownWords.Add(wordCode);
                    Dictionary[wordCode].Users.Add(listener.Id);
                    understood = true;
                }
            }

            if (understood)
            {
                speaker.Brain.Social(0.1);
                listener.Brain.Social(0.1);
                speaker.SkillSpeaking = Math.Min(1, speaker.SkillSpeaking + 0.002);
            }

            return new CommunicationResult
            {
                Success = understood,
                WordCode = wordCode,
                Concept = concept
            };
        }

        /// <summary>
        /// Обновление системы — забытые слова удаляются
        /// </summary>
        public void Update(double dt)
        {
            var toRemove = new List<int>();

            foreach (var kvp in Dictionary)
            {
                kvp.Value.Age += dt;

                // Слова без пользователей умирают
                if (kvp.Value.Users.Count == 0 && kvp.Value.Age > 100)
                    toRemove.Add(kvp.Key);

                // Старые неиспользуемые слова тоже
                if (kvp.Value.UsageCount < 3 && kvp.Value.Age > 200)
                    toRemove.Add(kvp.Key);

                // Сила слова затухает без использования
                kvp.Value.Strength = Math.Max(0, kvp.Value.Strength - dt * 0.0001);
            }

            foreach (var id in toRemove)
            {
                Dictionary.Remove(id);
                ExtinctWords++;
            }
        }

        /// <summary>
        /// Получить все слова, которые знает данный человек
        /// </summary>
        public List<(int Code, Concept Meaning)> GetKnownWords(Human human)
        {
            var result = new List<(int, Concept)>();
            foreach (var wc in human.KnownWords)
                if (Dictionary.ContainsKey(wc))
                    result.Add((wc, Dictionary[wc].PrimaryConcept));
            return result;
        }

        /// <summary>
        /// Языковая сложность племени
        /// </summary>
        public double TribalLanguageComplexity(List<int> memberIds)
        {
            int sharedWords = 0;
            foreach (var kvp in Dictionary)
            {
                int knowing = memberIds.Count(id => kvp.Value.Users.Contains(id));
                if (knowing > memberIds.Count / 2) sharedWords++;
            }
            return sharedWords;
        }
    }

    public class WordMeaning
    {
        public int Code;
        public Concept PrimaryConcept;
        public int CreatorId;
        public double Strength;      // Насколько закреплено слово
        public double Age;
        public int UsageCount;
        public HashSet<int> Users;   // Кто знает это слово
    }

    /// <summary>
    /// Базовые концепции для прото-языка
    /// </summary>
    public enum Concept
    {
        // Объекты
        Food, Water, Fire, Shelter, Tool, Weapon, Animal, Human,

        // Действия
        Go, Come, Eat, Drink, Sleep, Attack, Flee, Build, Give, Take,

        // Направления
        Here, There, Up, Down, Left, Right,

        // Опасности
        Danger, Predator, Storm, Cold, Hot,

        // Социальное
        Friend, Enemy, Leader, Child, Help, Together,

        // Эмоции
        Happy, Sad, Angry, Scared, Hungry, Tired,

        // Время
        Now, Before, After, Day, Night,

        // Количество
        One, Many, None, Big, Small,

        // Природа
        Tree, River, Mountain, Cave, Grass
    }

    public class CommunicationResult
    {
        public bool Success;
        public int WordCode;
        public Concept Concept;
        public bool Gesture; // Был жест вместо слова
    }
}