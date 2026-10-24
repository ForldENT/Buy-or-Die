using System;
using System.Collections.Generic;
using UnityEngine;

namespace BuyOrDie
{
    [Serializable] public class AssetSave
    {
        public double price, prevClose, anchor, pendingGap, evStartLog, evTargetLog;
        public bool evActive, limitUp, limitDown;
        public int evElapsed, evDuration, status, statusDay;
    }

    [Serializable] public class NewsSave { public int minute, tone, asset; public string headline; public bool breaking; }
    [Serializable] public class ScheduledSave { public int minute, template, asset, duration; public double mag; public bool fired; }
    [Serializable] public class HoldingSave { public int asset; public double qty, avg; }
    [Serializable] public class PositionSave { public int id, asset, lev, openedAt; public bool isLong; public double entry, margin, qty, liq; }
    [Serializable] public class LogSave { public int minute, asset; public string action; public bool positive, hasPnl; public double price, qty, pnl; }

    [Serializable] public class PortfolioSave
    {
        public double cash, realized, peak;
        public int trades, liqs, nextId;
        public List<HoldingSave> holdings;
        public List<PositionSave> positions;
        public List<LogSave> logs;
    }

    [Serializable] public class SaveData
    {
        public int version = 3;
        public string savedAt;
        public int minute, seed, day, selected;
        public double equity, dayStartEquity;
        public bool goalReached;
        public double domFactor, ovsFactor, cryFactor;
        public List<AssetSave> assets;
        public List<NewsSave> news;
        public List<ScheduledSave> schedule;
        public PortfolioSave portfolio;
        // 대폭락장 / 대호황장
        public int regime, regimeEnd, regimeStart = -1, regimePending, lastRegimeDay = -99, regimeWaveDay;
        public double volMult = 1;
    }

    /// <summary>
    /// 저장은 PlayerPrefs에 JSON 한 덩어리로 (WebGL에서도 동작). 차트 기록은 저장하지 않고 불러올 때 다시 그린다.
    /// </summary>
    public static class SaveSystem
    {
        const string Key = "bod_save_v2";

        public static bool HasSave { get { return PlayerPrefs.HasKey(Key); } }

        public static void Save(SaveData d)
        {
            d.savedAt = DateTime.Now.ToString("yyyy.MM.dd HH:mm");
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(d));
            PlayerPrefs.Save();
        }

        public static SaveData Load()
        {
            if (!HasSave) return null;
            try
            {
                var d = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(Key));
                if (d == null || d.assets == null || d.portfolio == null) return null;
                return d;
            }
            catch (Exception e)
            {
                Debug.LogWarning("세이브 파일을 읽지 못했습니다: " + e.Message);
                return null;
            }
        }

        public static void Delete()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }

    public enum LaunchMode { New, Load }

    /// <summary>타이틀 → 메인 게임으로 넘길 시작 방식.</summary>
    public static class GameLaunch
    {
        public static LaunchMode mode = LaunchMode.New;
        public static bool fromTitle;
    }

    /// <summary>플레이어 설정 (PlayerPrefs).</summary>
    public static class Settings
    {
        static float volume = -1;
        static int greenUp = -1;
        public static float Volume
        {
            get { if (volume < 0) volume = PlayerPrefs.GetFloat("set_volume", 0.7f); return volume; }
            set { volume = value; PlayerPrefs.SetFloat("set_volume", value); }
        }
        public static bool GreenUp
        {
            get { if (greenUp < 0) greenUp = PlayerPrefs.GetInt("set_greenup", 0); return greenUp == 1; }
            set { greenUp = value ? 1 : 0; PlayerPrefs.SetInt("set_greenup", greenUp); }
        }
        public static int DefaultSpeed { get { return PlayerPrefs.GetInt("set_speed", 1); } set { PlayerPrefs.SetInt("set_speed", value); } }
        public static bool AutoSave { get { return PlayerPrefs.GetInt("set_autosave", 1) == 1; } set { PlayerPrefs.SetInt("set_autosave", value ? 1 : 0); } }
        /// <summary>0 = 모든 속보, 1 = 보유 종목만, 2 = 끄기</summary>
        public static int NewsAlerts { get { return PlayerPrefs.GetInt("set_alerts", 0); } set { PlayerPrefs.SetInt("set_alerts", value); } }
        public static bool Fullscreen { get { return Screen.fullScreen; } set { Screen.fullScreen = value; } }

        static float music = -1;
        public static float MusicVolume
        {
            get { if (music < 0) music = PlayerPrefs.GetFloat("set_music", 0.45f); return music; }
            set { music = value; PlayerPrefs.SetFloat("set_music", value); MusicPlayer.ApplyVolume(); }
        }

        public static void Save() { PlayerPrefs.Save(); }
    }
}
