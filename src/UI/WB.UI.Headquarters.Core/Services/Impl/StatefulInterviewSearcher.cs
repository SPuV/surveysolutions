﻿using System;
using System.Collections.Generic;
using System.Linq;
using WB.Core.BoundedContexts.Headquarters.Views.Interview;
using WB.Core.GenericSubdomains.Portable;
using WB.Core.SharedKernels.DataCollection;
using WB.Core.SharedKernels.DataCollection.Aggregates;
using WB.Core.SharedKernels.DataCollection.Implementation.Aggregates.InterviewEntities;
using WB.Core.SharedKernels.SurveySolutions.Documents;
using WB.Enumerator.Native.WebInterview;
using WB.UI.Headquarters.API.WebInterview;

namespace WB.UI.Headquarters.Services.Impl
{
    // ReSharper disable once UnusedMember.Global Used by autoregistration
    public class StatefulInterviewSearcher : IStatefulInterviewSearcher
    {
        private readonly IInterviewFactory interviewFactory;
        private readonly IWebInterviewInterviewEntityFactory webNavigationService;

        public StatefulInterviewSearcher(IInterviewFactory interviewFactory, IWebInterviewInterviewEntityFactory webNavigationService)
        {
            this.interviewFactory = interviewFactory;
            this.webNavigationService = webNavigationService;
        }

        public SearchResults Search(IStatefulInterview interview, IQuestionnaire questionnaire, FilterOption[] flags, int skip, int take)
        {
            var nodesWithStats = GetFilteredNodesWithStats(flags, interview, questionnaire);

            int taken = 0, skipped = 0, total = 0;
            int searchResultId = 0;

            Identity lastSection = null;

            var results = new SearchResults
            {
                Stats = nodesWithStats.Stats
            };

            SearchResult currentResult = null;

            foreach (var node in nodesWithStats.Nodes)
            {
                IncrementSectionId();

                if (CanTake())
                {
                    if (currentResult == null || currentResult.SectionId != node.Parent.Identity.ToString())
                    {
                        if (currentResult != null)
                        {
                            results.Results.Add(currentResult);
                        }

                        currentResult = NewResult();
                    }
                
                    currentResult.Questions.Add(new Link
                    {
                        Target = node.Identity.ToString(),
                        Title = GetCurrentNodeTitle()
                    });

                    taken++;
                }
                else
                {
                    skipped++;
                }

                total++;

                SearchResult NewResult()
                {
                    return new SearchResult
                    {
                        Id = searchResultId,
                        SectionId = node.GetParentIdentity(questionnaire)?.ToString(),
                        Sections = node.GetBreadcrumbs(questionnaire).ToList()
                    };
                }

                // need to generate consistent search result id, independed on skip/take values
                void IncrementSectionId()
                {
                    if (lastSection == null)
                    {
                        lastSection = node.Parent.Identity;
                    }

                    if (lastSection != node.Parent.Identity)
                    {
                        lastSection = node.Parent.Identity;
                        searchResultId += 1;
                    }
                }

                string GetCurrentNodeTitle()
                {
                    switch (node)
                    {
                        case InterviewTreeQuestion q: return q.Title.ToString();
                        case InterviewTreeStaticText st: return st.Title.ToString();
                    }

                    return null;
                }
            }
            
            foreach (var ruleId in nodesWithStats.Rules)
            {
                if (CanTake())
                {
                    if (currentResult != null)
                    {
                        results.Results.Add(currentResult);
                    }

                    currentResult = new SearchResult
                    {
                        Id = searchResultId,
                        SectionId = "critical-rules",
                    };

                    var message = webNavigationService.GetCriticalRuleMessage(ruleId, interview, questionnaire, true);
                    currentResult.Questions.Add(new Link
                    {
                        Target = ruleId.FormatGuid(),
                        Title = message
                    });

                    taken++;
                }
                else
                {
                    skipped++;
                }

                total++;
            }
            
            bool CanTake()
            {
                if (skipped < skip) return false;
                if (taken < take) return true;

                return false;
            }

            if (currentResult != null)
            {
                results.Results.Add(currentResult);
            }

            results.TotalCount = total;

            return results;
        }

        public Dictionary<FilterOption, int> GetStatistics(IStatefulInterview interview, IQuestionnaire questionnaire)
        {
            var result = this.GetFilteredNodesWithStats(Array.Empty<FilterOption>(), interview, questionnaire);
            return result.Stats;
        }

        private static readonly Func<InterviewQuestionFilter, bool> FilteringRule = node => 
            node.Is(FilterOption.WithComments)
            && node.Or(FilterOption.Flagged, FilterOption.NotFlagged)
            && node.Or(FilterOption.Valid, FilterOption.Invalid)
            && node.Or(FilterOption.Answered, FilterOption.NotAnswered)
            && node.Or(FilterOption.ForSupervisor, FilterOption.ForInterviewer)
            && node.Or(FilterOption.CriticalQuestions, FilterOption.CriticalRules);

        private static readonly FilterOption[] AllFilterOptions =
            Enum.GetValues(typeof(FilterOption)).Cast<FilterOption>().ToArray();

        private FilteredNodesWithStats GetFilteredNodesWithStats(FilterOption[] flags, IStatefulInterview interview, IQuestionnaire questionnaire)
        {
            var result = new FilteredNodesWithStats();
            
            var flagged = interviewFactory.GetFlaggedQuestionIds(interview.Id);

            foreach (var option in AllFilterOptions)
            {
                result.Stats.Add(option, 0);
            }
            
            var rule = new InterviewQuestionFilter(
                new HashSet<Identity>(flagged), 
                FilteringRule, questionnaire);

            var nodes = interview.GetAllInterviewNodes()
                .Where(n => n is InterviewTreeQuestion || n is InterviewTreeStaticText);

            var flagsSet = new HashSet<FilterOption>(flags);

            foreach (var node in nodes)
            {
                rule.SetNode(node);
                var found = rule.Evaluate(flagsSet);

                if (found)
                {
                    result.Nodes.Add(node);
                    UpdateStats();
                }
            }

            if (flags.Length == 0 || flags.Contains(FilterOption.CriticalRules))
            {
                var failedCriticalRules = interview.GetFailedCriticalRules();
                result.Rules = failedCriticalRules.ToList();
                result.Stats[FilterOption.CriticalRules] = result.Rules.Count;
            }
            
            void UpdateStats()
            {
                foreach (var option in AllFilterOptions)
                {
                    var ruleValue = rule.Evaluate(option, false);
                    
                    if (ruleValue) result.Stats[option] += 1;
                }
            }

            return result;
        }
    }

    internal class FilteredNodesWithStats
    {
        public FilteredNodesWithStats()
        {
            Stats = new Dictionary<FilterOption, int>();
            Nodes = new List<IInterviewTreeNode>();
            Rules = new List<Guid>();
        }

        public List<Guid> Rules { get; set; }

        public Dictionary<FilterOption, int> Stats { get; }

        public List<IInterviewTreeNode> Nodes { get; }

    }
}
