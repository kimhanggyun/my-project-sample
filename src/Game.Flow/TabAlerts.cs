using System.Collections.Generic;
using Game.Campaign;
using Game.Data.Defs;

namespace Game.Flow
{
    /// <summary>
    /// 탭 알림 빨간 점 (main-screen.md, E21): 그 탭에서 지금 할 수 있는 일이 있으면 탭 id를 돌려준다.
    /// 규칙만 보고 정한다 (화면은 점만 그린다). 탭이 늘면 여기에 한 줄.
    /// </summary>
    public static class TabAlerts
    {
        public static HashSet<string> Compute(DefinitionSet defs, CampaignState s, StoryFlow flow, long nowUnixSeconds)
        {
            var on = new HashSet<string>();
            // 스토리: 열렸는데 아직 깨지 않은 스텝
            foreach (var id in defs.StepIndex.Order)
                if (!s.ClearedSteps.Contains(id) && flow.IsUnlocked(s, id)) { on.Add("story"); break; }
            // 장수: 가진 스킬북으로 배울 수 있는 스킬, 한계 돌파 가능
            var books = new SkillBookService(defs);
            var lb = new LimitBreakService(defs);
            foreach (var g in s.Generals)
            {
                if (lb.Check(s, g.GeneralId) == LimitBreakService.Ok) { on.Add("general"); break; }
                bool found = false;
                foreach (var kv in s.SkillBooks)
                    if (kv.Value > 0 && books.Check(s, g.GeneralId, kv.Key) == LearnCheck.Ok) { found = true; break; }
                if (found) { on.Add("general"); break; }
            }
            // 포로: 처리할 포로가 있음
            if (s.Prisoners.Count > 0) on.Add("prisoner");
            // 부대 돌보기: 부상병이 있거나 편성 한도까지 보충할 자리가 있음
            foreach (var u in s.Units) if (u.Wounded > 0) { on.Add("care"); break; }
            if (!on.Contains("care") && new TroopService(defs).RefillPlan(s).Count > 0) on.Add("care");
            // 연습 대결: 오늘 남은 횟수
            if (defs.Practice.DailyLimit > 0 && PracticeService.TodayCount(s, nowUnixSeconds) < defs.Practice.DailyLimit) on.Add("practice");
            return on;
        }
    }
}
