﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

using MahjongAI.Models;

namespace MahjongAI
{
    partial class AIController : Controller
    {
        private const int defaultDepth = 3;

        private Strategy strategy;

        public AIController(PlatformClient client, Strategy strategy = null): base(client)
        {
            MahjongHelper.getInstance();
            this.strategy = strategy ?? new Strategy();
        }

        public override void OnDraw(Tile tile)
        {
            int distance = calcDistance();
            if (distance == -1 && (player.fuuro.VisibleCount == 0 || calcPoint(tile, riichi: false, tsumoAgari: false, isLastTile: gameData.remainingTile == 0) > 0))
            {
                client.Tsumo();
            }
            else if (player.reached)
            {
                if (shouldAnKan(tile))
                {
                    client.Ankan(tile);
                }
                else
                {
                    client.Discard(tile);
                }
            }
            else {
                decideMove();
            }
        }

        public override void OnWait(Tile tile, Player fromPlayer)
        {
            fromPlayer.graveyard.Add(tile);
            EvalResult currentEvalResult = eval13();
            fromPlayer.graveyard.Remove(tile);
            player.hand.Add(tile);
            EvalResult currentEvalResult14 = eval14(tile, 1, isLastTile: gameData.remainingTile == 0);
            player.hand.Remove(tile);
            if (currentEvalResult14.Distance == -1 && !currentEvalResult.Furiten && currentEvalResult14.E_Point > 0)
            {
                client.Ron();
            }
            else
            {
                Trace.WriteLine("Distance: " + currentEvalResult.Distance);
                Trace.WriteLine(string.Format("Option: pass, E_PromotionCount: [{0}], E_Point: {1}", currentEvalResult.E_PromotionCount.ToString(", ", c => c.ToString("F0")), currentEvalResult.E_Point));
                player.hand.Add(tile);

                List<FuuroGroup> candidates = new List<FuuroGroup>()
                {
                    tryGetFuuroGroup(FuuroType.pon, new[] { Tuple.Create(tile.GenaralId, 2) }, tile)
                };
                if (gameData.remainingTile > 0)
                {
                    candidates.Add(tryGetFuuroGroup(FuuroType.minkan, new[] { Tuple.Create(tile.GenaralId, 3) }, tile));
                }
                if (fromPlayer.id == 3 && tile.Type != "z")
                {
                    candidates.AddRange(new List<FuuroGroup>()
                    {
                        tryGetFuuroGroup(FuuroType.chii, new[] { Tuple.Create(tile.GenaralId + 1, 1), Tuple.Create(tile.GenaralId + 2, 1) }, tile),
                        tryGetFuuroGroup(FuuroType.chii, new[] { Tuple.Create(tile.GenaralId - 1, 1), Tuple.Create(tile.GenaralId + 1, 1) }, tile),
                        tryGetFuuroGroup(FuuroType.chii, new[] { Tuple.Create(tile.GenaralId - 2, 1), Tuple.Create(tile.GenaralId - 1, 1) }, tile)
                    });
                }
                Tuple<FuuroGroup, Tile, EvalResult> bestResult = Tuple.Create<FuuroGroup, Tile, EvalResult>(null, null, currentEvalResult);

                foreach (var candidate in candidates)
                {
                    if (candidate == null)
                    {
                        continue;
                    }

                    Trace.WriteLine(string.Format("Option: {0}", candidate.type.ToString()));
                    player.hand.RemoveRange(candidate);
                    player.fuuro.Add(candidate);
                    Tile discardTile = null;
                    EvalResult tmpResult;
                    if (candidate.type != FuuroType.minkan)
                    {
                        discardTile = chooseDiscardForAtk(out tmpResult);

                        // 不能食替
                        if (discardTile != null 
                            && (discardTile.GenaralId == tile.GenaralId 
                                || discardTile.Type == tile.Type && discardTile.GenaralId == tile.GenaralId - 3 && candidate.Exists(t => t.GenaralId == tile.GenaralId - 2) && candidate.Exists(t => t.GenaralId == tile.GenaralId - 1)
                                || discardTile.Type == tile.Type && discardTile.GenaralId == tile.GenaralId + 3 && candidate.Exists(t => t.GenaralId == tile.GenaralId + 2) && candidate.Exists(t => t.GenaralId == tile.GenaralId + 1)))
                        {
                            tmpResult = null;
                        }
                    }
                    else
                    {
                        tmpResult = eval13();
                    }
                    if (tmpResult != null && !shouldDef(tmpResult, discardTile) && 
                        (shouldNaki(currentEvalResult, tmpResult) && new EvalResultComp(!isAllLastTop()).Compare(tmpResult, bestResult.Item3) > 0 // All last top 不比较得点
                        || gameData.remainingTile < 16 && currentEvalResult.Distance == 1 && tmpResult.Distance == 0)) // 特判最后四巡鸣型听 
                    {
                        bestResult = Tuple.Create(candidate, discardTile, tmpResult);
                    }
                    player.fuuro.Remove(candidate);
                    player.hand.AddRange(candidate);
                }

                player.hand.Remove(tile);
                Trace.WriteLine(string.Format("BestResult: {0}, E_PromotionCount: [{1}], E_Point: {2}, Distance: {3}", bestResult.Item1 != null ? bestResult.Item1.type.ToString() : "pass", bestResult.Item3.E_PromotionCount.ToString(", ", c => c.ToString("F0")), bestResult.Item3.E_Point, bestResult.Item3.Distance));

                if (bestResult.Item1 != null)
                {
                    switch (bestResult.Item1.type)
                    {
                        case FuuroType.chii:
                            client.Chii(bestResult.Item1[0], bestResult.Item1[1]);
                            break;
                        case FuuroType.pon:
                            client.Pon(bestResult.Item1[0], bestResult.Item1[1]);
                            break;
                        case FuuroType.minkan:
                            client.Minkan();
                            break;
                    }
                }
                else
                {
                    client.Pass();
                }
            }
        }

        protected override void OnNaki(Player currentPlayer, FuuroGroup fuuro)
        {
            if (currentPlayer == player && (fuuro.type == FuuroType.pon || fuuro.type == FuuroType.chii))
            {
                decideMove();
            }
        }

        private void decideMove() {
            EvalResult evalResult;
            List<Tuple<Tile, EvalResult>> options;
            Tile discardTile = chooseDiscardForAtk(out options, out evalResult);
            if (!shouldDef(evalResult, discardTile))
            {
                if (shouldAnKan(discardTile))
                {
                    client.Ankan(discardTile);
                }
                else if (shouldKaKan(discardTile))
                {
                    client.Kakan(discardTile);
                }
                else if (shouldReach(discardTile, evalResult))
                {
                    client.Reach(discardTile);
                    client.Discard(discardTile);
                }
                else {
                    client.Discard(discardTile);
                }
            }
            else
            {
                Tile newDiscardTile = chooseDiscardForDef(options);
                client.Discard(newDiscardTile);
            }
        }

        private bool shouldReach(Tile discardTile, EvalResult evalResult)
        {
            bool changeRanking = false;
            int rankingWithReach = 0;
            int rankingWithoutReach = 0;

            player.hand.Remove(discardTile);
            player.graveyard.Add(discardTile);
            EvalResult evalResultWithoutReach = eval13(1, false);
            if (isAllLast())
            {
                rankingWithReach = expectedRankingWhenAgari(riichi: true);
                rankingWithoutReach = expectedRankingWhenAgari(riichi: false);
                changeRanking = rankingWithReach < rankingWithoutReach;
                Trace.WriteLine(string.Format("Expected Ranking: w/ reach {0}, w/o reach {1}", rankingWithReach, rankingWithoutReach));
            }
            player.graveyard.Remove(discardTile);
            player.hand.Add(discardTile);

            return !player.reached && player.fuuro.VisibleCount == 0 && gameData.remainingTile >= 4 && evalResult.Distance == 0
                && (evalResultWithoutReach.E_Point < 6000 || gameData.players.Count(p => p.reached) >= 2) // 期望得点<6000 或 立直人数 >=2
                && evalResult.E_PromotionCount[0] > 0 // 没有空听
                && !shouldDef(evalResult) // 没有在防守状态
                && !(isAllLast() && !changeRanking && !(player.direction == Direction.E && rankingWithoutReach > 1) && evalResultWithoutReach.E_Point > 0) // 如果All last且立直不改变顺位，除非是尾亲且和了也不是top，否则不立直
                && evalResult.E_PromotionCount[0] > 1; // 枚数只剩1张就不立直
        }

        private int expectedRankingWhenAgari(bool riichi)
        {
            int res = gameData.getRankingByPlayer(player);
            double extraPoint = gameData.seq2 * 100 + gameData.players.Count(p => p.reached) * 1000 + gameData.reachStickCount * 1000;
            for (var i = res - 1; i >= 1; i--)
            {
                var target = gameData.getPlayerByRanking(i);

                // target放铳
                var point = eval13(1, riichi, tsumo: false).E_Point;
                if (player.point + point + extraPoint + gameData.seq2 * 300 < target.point - point - gameData.seq2 * 300)
                {
                    break;
                }

                // 自摸
                point = eval13(1, riichi, tsumo: true).E_Point;
                if (player.point + point + extraPoint + gameData.seq2 * 300 < target.point - point * (player.direction == Direction.E ? 0.33 : target.direction == Direction.E ? 0.5 : 0.25) - gameData.seq2 * (target.direction == Direction.E ? 2 : 1))
                {
                    break;
                }

                res = i;
            }

            return res;
        }

        private bool shouldAnKan(Tile tile)
        {
            FuuroGroup group = tryGetFuuroGroup(FuuroType.ankan, new[] { Tuple.Create(tile.GenaralId, 4) });
            if (group == null || gameData.remainingTile <= 0)
            {
                return false;
            }

            int distance = calcDistance();
            player.fuuro.Add(group);
            player.hand.RemoveRange(group);

            int newDistance = calcDistance();
            bool res = false;
            if (newDistance == distance)
            {
                res = true;
            }

            player.fuuro.Remove(group);
            player.hand.AddRange(group);

            return res;
        }

        private bool shouldKaKan(Tile tile)
        {
            return gameData.remainingTile > 0 && player.fuuro.Any(group => group.type == FuuroType.pon && group.All(t => t.GenaralId == tile.GenaralId));
        }

        private bool shouldNaki(EvalResult resultBefore, EvalResult resultAfter)
        {
            return resultBefore.Distance >= 3 && hasYakuhai()
                || resultBefore.Distance <= 2 && resultAfter.E_Point > resultBefore.E_Point && resultAfter.E_Point > 0
                || resultBefore.Distance <= 2 && resultAfter.E_Point > resultBefore.E_Point * (0.8 - (17 - gameData.remainingTile / 4) * 0.02) && resultAfter.E_Point > 0
                    && resultBefore.Distance > resultAfter.Distance
                || resultBefore.Distance <= 2 && (resultAfter.E_Point > resultBefore.E_Point * (0.4 - (17 - gameData.remainingTile / 4) * 0.02) || (resultBefore.E_Point - resultAfter.E_Point) < 2000) && resultAfter.E_Point > 0
                    && resultBefore.Distance > resultAfter.Distance && (resultBefore.E_PromotionCount[1] <= resultAfter.E_PromotionCount[0] || resultBefore.VisibleFuuroCount > 0)
                || isAllLastTop() && hasYakuhai(); // All last top 速攻
        }

        public Tile chooseDiscardForAtk(int depth = -1)
        {
            EvalResult evalResult;
            return chooseDiscardForAtk(out evalResult, depth);
        }

        private Tile chooseDiscardForAtk(out EvalResult evalResult, int depth = -1)
        {
            List<Tuple<Tile, EvalResult>> options;
            return chooseDiscardForAtk(out options, out evalResult, depth);
        }

        private Tile chooseDiscardForAtk(out List<Tuple<Tile, EvalResult>> options, out EvalResult evalResult, int depth = -1)
        {
            EvalResultComp evalResultComp = new EvalResultComp(!isAllLastTop()); // All last top 不比较得点
            var handTmp = new List<Tile>(player.hand);
            var evalResults = new Dictionary<string, EvalResult>();
            Tuple<Tile, EvalResult> bestResult = null;
            int currentNormalDistance;
            var currentDistance = calcDistance(out currentNormalDistance);
            options = null;
            if (depth == -1)
            {
                options = new List<Tuple<Tile, EvalResult>>();
                Trace.WriteLine("Distance: " + currentDistance);
            }

            foreach (var tile in handTmp)
            {
                if (!evalResults.ContainsKey(tile.Name))
                {
                    player.hand.Remove(tile);
                    player.graveyard.Add(tile);
                    int tmpNormalDistance;
                    var tmpDistance = calcDistance(out tmpNormalDistance);
                    if (tmpDistance <= currentDistance || depth == -1 && currentNormalDistance <= currentDistance + 1 && tmpNormalDistance <= currentNormalDistance) // 打掉后向听数不变，或者有希望做一般型且打掉后一般型的向听数不变
                    {
                        var result = evalResults[tile.Name] = eval13(depth);
                        result.DiscardedDoraCount = doraValue(tile);
                        if (depth == -1)
                        {
                            options.Add(Tuple.Create(tile, result));
                            Trace.WriteLine(string.Format("Option: discard {0}, E_PromotionCount: [{1}]{4}, E_Point: {2}{3}", 
                                tile.Name, 
                                result.E_PromotionCount.ToString(", ", c => c.ToString("F0")), 
                                result.E_Point, 
                                result.Distance > currentDistance ? ", Distance++" : "",
                                result.E_NormalPromitionCount != result.E_PromotionCount[0] ? "(" + result.E_NormalPromitionCount + ")" : ""));
                        }
                    }
                    else
                    {
                        evalResults[tile.Name] = null;
                    }
                    player.hand.Add(tile);
                    player.graveyard.Remove(tile);
                }
            }

            foreach (var tile in handTmp)
            {
                var result = evalResults[tile.Name];

                if (result == null)
                {
                    continue;
                }

                if (bestResult == null 
                    || evalResultComp.Compare(result, bestResult.Item2) > 0 
                    || evalResultComp.Compare(result, bestResult.Item2) == 0 && getWeight(tile) < getWeight(bestResult.Item1))
                {
                    bestResult = new Tuple<Tile, EvalResult>(tile, result);
                }
            }

            if (depth == -1)
            {
                if (bestResult == null || bestResult.Item1 == null || bestResult.Item2 == null)
                {
                    Trace.WriteLine("BestResult: null");
                }
                else
                {
                    Trace.WriteLine(string.Format("BestResult: discard {0}, E_PromotionCount: [{1}]{4}, E_Point: {2}, Distance: {3}",
                    bestResult.Item1.Name,
                    bestResult.Item2.E_PromotionCount.ToString(", ", c => c.ToString("F0")),
                    bestResult.Item2.E_Point,
                    bestResult.Item2.Distance,
                    bestResult.Item2.E_NormalPromitionCount != bestResult.Item2.E_PromotionCount[0] ? "(" + bestResult.Item2.E_NormalPromitionCount + ")" : ""));
                }
            }
            
            if (bestResult == null)
            {
                evalResult = null;
                return null;
            }

            evalResult = bestResult.Item2;
            return bestResult.Item1;
        }

        private EvalResult eval13(int depth = -1, bool riichi = true, bool tsumo = false)
        {
            var res = new EvalResult();
            int normalDistance;
            res.Distance = calcDistance(out normalDistance);
            res.NormalDistance = normalDistance;
            if (depth == -1)
            {
                if (res.Distance > defaultDepth - 1) // 如果向听数高就减少搜索深度
                {
                    depth = defaultDepth - 1;
                }
                else
                {
                    depth = defaultDepth;
                }
            }
            res.DoraInFuuroCount = player.fuuro.Tiles.Sum(t => doraValue(t));
            res.DoraCount = player.hand.Sum(t => doraValue(t)) + res.DoraInFuuroCount;
            res.VisibleFuuroCount = player.fuuro.VisibleCount;
            res.KanCount = player.fuuro.Count(f => f.type == FuuroType.ankan || f.type == FuuroType.kakan || f.type == FuuroType.minkan);
            var promotionTiles = new List<Tuple<Tile, EvalResult>>();
            var evalResults = new Dictionary<string, EvalResult>();
            var normalDistanceResults = new Dictionary<string, int>();
            var candidates = new HashSet<string>();
            var visibleTiles = getVisibleTiles().ToList();
            for (var i = 0; i < Tile.TotalCount; i++)
            {
                if (!visibleTiles.Exists(t => t.Id == i))
                {
                    var tile = new Tile(i);
                    EvalResult result;
                    if (!evalResults.TryGetValue(tile.Name, out result))
                    {
                        player.hand.Add(tile);

                        int tmpNormalDistance;
                        if (calcDistance(out tmpNormalDistance) < res.Distance)
                        {
                            result = evalResults[tile.Name] = eval14(tile, depth, riichi, tsumo);
                        }
                        else
                        {
                            result = evalResults[tile.Name] = null;
                        }

                        normalDistanceResults[tile.Name] = tmpNormalDistance;
                        player.hand.Remove(tile);
                    }
                    if (normalDistanceResults[tile.Name] < res.NormalDistance && normalDistanceResults[tile.Name] <= res.Distance)
                    {
                        res.E_NormalPromitionCount++;
                    }
                    if (result != null)
                    {
                        promotionTiles.Add(Tuple.Create(tile, result));
                    }
                }
            }

            res.Furiten = promotionTiles.Exists(tuple => player.graveyard.Exists(t => t.GenaralId == tuple.Item1.GenaralId));

            if (promotionTiles.Count > 0)
            {
                res.E_Point = promotionTiles.Sum(tuple => tuple.Item2.E_Point) / promotionTiles.Count; // 计算期望得点（如果有得点为0的情况，那么也计入，拉低期望得点）
            }

            if (res.Distance < depth) // 在可以算出得点的情况下
            {
                promotionTiles.RemoveAll(tuple => tuple.Item2.E_Point <= 0); // 把得点为0的情况去掉（不算进张）
            }
            
            res.E_PromotionCount = new List<double>() { promotionTiles.Count };
            for (var i = 0; i < Math.Min(depth - 1, res.Distance); i++)
            {
                res.E_PromotionCount.Add(promotionTiles.Count > 0 ? promotionTiles.Sum(tuple => tuple.Item2.E_PromotionCount[i]) / promotionTiles.Count : 0);
            }
            
            return res;
        }

        private EvalResult eval14(Tile lastTile, int depth, bool riichi = true, bool tsumo = false, bool isLastTile = false)
        {
            var res = new EvalResult();
            int normalDistance;
            res.Distance = calcDistance(out normalDistance);
            res.NormalDistance = normalDistance;
            if (depth <= 1 || res.Distance == -1)
            {
                if (res.Distance == -1)
                {
                    res.E_Point = calcPoint(lastTile, riichi, tsumo, isLastTile);
                }
                res.E_PromotionCount = new List<double>();
            }
            else
            {
                EvalResult tmpResult;
                chooseDiscardForAtk(out tmpResult, depth - 1);
                res.E_Point = tmpResult.E_Point;
                res.E_PromotionCount = tmpResult.E_PromotionCount;
            }
            return res;
        }

        private class EvalResultComp : IComparer<EvalResult>
        {
            private E_PromotionCountComp e_PromotionComp = new E_PromotionCountComp();
            private bool comparePoint;

            public EvalResultComp(bool comparePoint)
            {
                this.comparePoint = comparePoint;
            }

            public int Compare(EvalResult x, EvalResult y)
            {
                int res = 0;
                if ((y == null) && (x == null))
                {
                    return 0;
                }
                else if ((res = (y == null).CompareTo(x == null)) != 0)
                {
                    return res;
                }
                else if (x.Distance == y.Distance && x.Distance >= 2
                    && (res = y.NormalDistance.CompareTo(x.NormalDistance)) != 0) // 如果两个都不是一般型选有希望做一般型的那个。如果有一个是一般型，选一般型的那个。（后一句只对鸣牌判断有效，对摸牌判断在由于进张数不同，本来就可以正常处理）
                {
                    return res;
                }
                else if (x.Distance == y.Distance && x.Distance >= 2 && x.NormalDistance == y.NormalDistance && x.NormalDistance == x.Distance + 1 
                    && (res = comparePromotionCount0(x.E_NormalPromitionCount, x.DiscardedDoraCount, y.E_NormalPromitionCount, y.DiscardedDoraCount)) != 0) // 如果两个都不是一般型且都有希望做一般型，比较一般型的进张数
                {
                    return res;
                }
                else if (y.Distance >= 2 && x.Distance == y.Distance + 1 && x.NormalDistance <= y.NormalDistance && x.E_PromotionCount.Count > 1 && x.E_PromotionCount[1] >= y.E_PromotionCount[0] * 2
                    && (res = comparePromotionCount0(x.E_NormalPromitionCount, x.DiscardedDoraCount, y.E_NormalPromitionCount, y.DiscardedDoraCount)) != 0) // 如果x是一般型但y不是，且x形状比y好很多，比较一般型的进张数
                {
                    return res;
                }
                else if (x.Distance >= 2 && y.Distance == x.Distance + 1 && y.NormalDistance <= x.NormalDistance && y.E_PromotionCount.Count > 1 && y.E_PromotionCount[1] >= x.E_PromotionCount[0] * 2
                    && (res = comparePromotionCount0(x.E_NormalPromitionCount, x.DiscardedDoraCount, y.E_NormalPromitionCount, y.DiscardedDoraCount)) != 0) // 如果y是一般型但x不是，且y形状比x好很多，比较一般型的进张数
                {
                    return res;
                }
                else if ((res = y.Distance.CompareTo(x.Distance)) != 0) // 向听数
                {
                    return res;
                }
                else if (x.Distance <= 2 && (res = y.Furiten.CompareTo(x.Furiten)) != 0) // 是否振听
                {
                    return res;
                }
                else if (comparePoint && (res = (x.E_Point * x.E_PromotionCount.Product(n => n)).CompareTo(y.E_Point * y.E_PromotionCount.Product(n => n))) != 0) // 期望得点数 * 进张数的积，如comparePoint为false则不比较
                {
                    return res;
                }
                else if ((res = (y.VisibleFuuroCount > 0).CompareTo(x.VisibleFuuroCount > 0)) != 0) // 是否门清
                {
                    return res;
                }
                else if (x.E_PromotionCount.Count > 0 && (res = comparePromotionCount0(x.E_PromotionCount[0], x.DiscardedDoraCount, y.E_PromotionCount[0], y.DiscardedDoraCount)) != 0) // 进张数
                {
                    return res;
                }
                else if (x.E_PromotionCount.Count > 0 && (res = comparePromotionCount0(x.E_NormalPromitionCount, x.DiscardedDoraCount, y.E_NormalPromitionCount, y.DiscardedDoraCount)) != 0) // 一般型进张数
                {
                    return res;
                }
                else if ((res = x.DoraCount.CompareTo(y.DoraCount)) != 0) // dora数
                {
                    return res;
                }
                else if ((res = x.DoraInFuuroCount.CompareTo(y.DoraInFuuroCount)) != 0) // 副露中的dora数
                {
                    return res;
                }
                else if ((res = e_PromotionComp.Compare(x.E_PromotionCount, y.E_PromotionCount)) != 0) // 后几层的进张数
                {
                    return res;
                }
                else if ((res = x.KanCount.CompareTo(y.KanCount)) != 0) // 杠的数量
                {
                    return res;
                }
                else
                {
                    return 0;
                }
            }

            private int comparePromotionCount0(double x, int xDiscardedDoraCount, double y, int yDiscardedDoraCount)
            {
                if (xDiscardedDoraCount > yDiscardedDoraCount) // 如果打掉的牌是dora，打掉的话视为进张数少1
                {
                    return (x - 1).CompareTo(y);
                }
                else if (xDiscardedDoraCount < yDiscardedDoraCount)
                {
                    return (x + 1).CompareTo(y);
                }
                else
                {
                    return x.CompareTo(y);
                }
            }
        }

        private class E_PromotionCountComp : IComparer<List<double>>
        {
            public int Compare(List<double> x, List<double> y)
            {
                for (int i = 0; i < x.Count; i++)
                {
                    if (x[i].CompareTo(y[i]) != 0)
                    {
                        return x[i].CompareTo(y[i]);
                    }
                }

                return 0;
            }
        }
    }
}
