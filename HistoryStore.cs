using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace ClipboardKeeper
{
    public class HistoryItem
    {
        public string Text { get; set; }
        public DateTime At { get; set; }
    }

    // 核心逻辑：追加记录、重复内容置顶、上限截断、JSON 持久化
    public class HistoryStore
    {
        public const int MaxItems = 1000;

        public List<HistoryItem> Items { get; private set; }
        public string Path { get; private set; }
        public int Cap { get; private set; }

        public HistoryStore(string path, int cap = MaxItems)
        {
            Path = path;
            Cap = cap;
            Items = new List<HistoryItem>();
        }

        public void Load()
        {
            Items.Clear();
            if (!File.Exists(Path)) return; // 首次运行：空历史，不是错误
            try
            {
                var ser = new JavaScriptSerializer();
                var loaded = ser.Deserialize<List<HistoryItem>>(File.ReadAllText(Path));
                if (loaded != null) Items = loaded;
            }
            catch (Exception)
            {
                // 文件损坏时不让程序崩溃，历史视为空
                Items = new List<HistoryItem>();
            }
        }

        // 新复制的内容：已存在则移到最前并更新时间，否则插入最前；超过上限丢弃最旧
        public void Add(string text)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].Text == text)
                {
                    var found = Items[i];
                    Items.RemoveAt(i);
                    found.At = DateTime.Now;
                    Items.Insert(0, found);
                    Trim();
                    return;
                }
            }
            Items.Insert(0, new HistoryItem { Text = text, At = DateTime.Now });
            Trim();
        }

        public void Save()
        {
            var ser = new JavaScriptSerializer();
            File.WriteAllText(Path, ser.Serialize(Items));
        }

        public void Clear()
        {
            Items.Clear();
        }

        private void Trim()
        {
            while (Items.Count > Cap) Items.RemoveAt(Items.Count - 1);
        }
    }
}
