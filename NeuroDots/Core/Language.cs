using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuroCivilization.Core
{
    /// <summary>
    /// Система протоязыка — слова возникают и эволюционируют,
    /// значения закрепляются через социальное взаимодействие.
    /// Оптимизирована без аллокаций в критических методах.
    /// </summary>
    public class LanguageSystem
    {
        public Dictionary<int, WordMeaning> Dictionary = new();
        private readonly List<int> _toRemove = new(32);

        private int _nextWordCode = 1;

        public int TotalWordsCreated;
        public int ActiveWords => Dictionary.Count;
        public int ExtinctWords;

        private static Random R => Random.Shared;

        private static readonly string[] Syllables =
        {
            "ak", "ba", "da", "el", "fa", "go", "ha", "ik", "ja", "ka",
            "lu", "ma", "ni", "or", "pa", "ra", "sa", "tu", "ul", "va", "ze"
        };

        public int CreateWord(Concept concept, int creatorId)
        {
            int code = _nextWordCode++;
            string phonetic = Syllables[R.Next(Syllables.Length)] +
                              (R.NextDouble() < 0.65 ? Syllables[R.Next(Syllables.Length)] : "");
            Dictionary[code] = new WordMeaning
            {
                Code = code,
                Phonetic = phonetic,
                PrimaryConcept = concept,
                CreatorId = creatorId,
                Strength = 0.35,
                Age = 0,
                UsageCount = 1,
                Users = new HashSet<int> { creatorId }
            };
            TotalWordsCreated++;
            return code;
        }

        public void UseWord(int wordCode, int speakerId, List<int> listenerIds, double speakerSkill)
        {
            if (!Dictionary.TryGetValue(wordCode, out var word)) return;

            word.UsageCount++;
            word.Strength = Math.Min(1.0, word.Strength + 0.015);
            word.Users.Add(speakerId);

            if (listenerIds != null)
            {
                for (int i = 0; i < listenerIds.Count; i++)
                {
                    if (R.NextDouble() < speakerSkill * 0.3)
                        word.Users.Add(listenerIds[i]);
                }
            }
        }

        public CommunicationResult Communicate(Human speaker, Human listener, Concept concept)
        {
            if (speaker == null || listener == null) return default;

            int wordCode = -1;
            for (int i = 0; i < speaker.KnownWords.Count; i++)
            {
                int wc = speaker.KnownWords[i];
                if (Dictionary.TryGetValue(wc, out var wm) && wm.PrimaryConcept == concept)
                {
                    wordCode = wc;
                    break;
                }
            }

            if (wordCode == -1)
            {
                double createChance = Math.Max(0.04, speaker.Genes.EffCreativity * (0.3 + speaker.SkillSpeaking) * 0.25);
                if (R.NextDouble() < createChance)
                {
                    wordCode = CreateWord(concept, speaker.Id);
                    speaker.LearnWord(wordCode);
                    speaker.SkillSpeaking = Math.Min(1.0, speaker.SkillSpeaking + 0.008);
                }
                else
                {
                    return new CommunicationResult { Success = false, Gesture = true, Concept = concept };
                }
            }

            if (!Dictionary.TryGetValue(wordCode, out var word))
                return new CommunicationResult { Success = false, Gesture = true, Concept = concept };

            word.UsageCount++;
            word.Strength = Math.Min(1.0, word.Strength + 0.02);

            bool understood = false;
            if (listener.KnowsWord(wordCode))
            {
                understood = true;
            }
            else
            {
                double understandChance = Math.Clamp(
                    listener.Genes.LearningSpeed * (0.35 + speaker.SkillSpeaking) * 0.45, 0.08, 0.85);
                if (R.NextDouble() < understandChance)
                {
                    listener.LearnWord(wordCode);
                    word.Users.Add(listener.Id);
                    understood = true;
                }
            }

            if (understood)
            {
                speaker.Brain.Social(0.12);
                listener.Brain.Social(0.12);
                speaker.SkillSpeaking = Math.Min(1.0, speaker.SkillSpeaking + 0.003);
                listener.SkillSpeaking = Math.Min(1.0, listener.SkillSpeaking + 0.002);
            }

            return new CommunicationResult
            {
                Success = understood,
                WordCode = wordCode,
                Concept = concept
            };
        }

        public void Update(double dt)
        {
            _toRemove.Clear();

            foreach (var kvp in Dictionary)
            {
                var w = kvp.Value;
                w.Age += dt;

                if (w.Users.Count == 0 && w.Age > 150)
                {
                    _toRemove.Add(kvp.Key);
                    continue;
                }

                if (w.UsageCount < 2 && w.Age > 300)
                {
                    _toRemove.Add(kvp.Key);
                    continue;
                }

                w.Strength = Math.Max(0.0, w.Strength - dt * 0.00005);
            }

            for (int i = 0; i < _toRemove.Count; i++)
            {
                Dictionary.Remove(_toRemove[i]);
                ExtinctWords++;
            }
        }

        public List<(int Code, Concept Meaning)> GetKnownWords(Human human)
        {
            var result = new List<(int, Concept)>(human.KnownWords.Count);
            for (int i = 0; i < human.KnownWords.Count; i++)
            {
                int wc = human.KnownWords[i];
                if (Dictionary.TryGetValue(wc, out var wm))
                    result.Add((wc, wm.PrimaryConcept));
            }
            return result;
        }

        public double TribalLanguageComplexity(List<int> memberIds)
        {
            if (memberIds == null || memberIds.Count == 0) return 0;
            int sharedWords = 0;
            int half = memberIds.Count / 2;
            foreach (var kvp in Dictionary)
            {
                int knowing = 0;
                var users = kvp.Value.Users;
                for (int i = 0; i < memberIds.Count; i++)
                {
                    if (users.Contains(memberIds[i])) knowing++;
                }
                if (knowing > half) sharedWords++;
            }
            return sharedWords;
        }
    }

    public class WordMeaning
    {
        public int Code;
        public string Phonetic = "";
        public Concept PrimaryConcept;
        public int CreatorId;
        public double Strength;
        public double Age;
        public int UsageCount;
        public HashSet<int> Users = new();
    }

    public enum Concept
    {
        Food, Water, Fire, Shelter, Tool, Weapon, Animal, Human,
        Go, Come, Eat, Drink, Sleep, Attack, Flee, Build, Give, Take,
        Here, There, Up, Down, Left, Right,
        Danger, Predator, Storm, Cold, Hot,
        Friend, Enemy, Leader, Child, Help, Together,
        Happy, Sad, Angry, Scared, Hungry, Tired,
        Now, Before, After, Day, Night,
        One, Many, None, Big, Small,
        Tree, River, Mountain, Cave, Grass
    }

    public struct CommunicationResult
    {
        public bool Success;
        public int WordCode;
        public Concept Concept;
        public bool Gesture;
    }
}
