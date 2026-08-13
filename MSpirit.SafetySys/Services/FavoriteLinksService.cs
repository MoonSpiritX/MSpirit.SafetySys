// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MS_SafetyManagementSystem.Models;
using Serilog;

namespace MS_SafetyManagementSystem.Services
{
    public class FavoriteLinksService
    {
        private readonly string _filePath;
        private List<LinkCategory> _categories = new();

        public FavoriteLinksService(string rootPath)
        {
            _filePath = Path.Combine(rootPath, "FavoriteLinks.json");
            Load();
        }

        private void Load()
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    var json = File.ReadAllText(_filePath);
                    _categories = JsonSerializer.Deserialize<List<LinkCategory>>(json) ?? new List<LinkCategory>();
                    // 确保每个链接有正确的 Order
                    foreach (var cat in _categories)
                    {
                        int order = 1;
                        foreach (var link in cat.Links.OrderBy(l => l.Order))
                        {
                            link.Order = order++;
                        }
                    }

                    // 检查并补充缺失的分类（如办公网址）
                    EnsureDefaultCategories();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "加载 FavoriteLinks.json 失败");
                    _categories = GetDefaultCategories();
                    Save();
                }
            }
            else
            {
                _categories = GetDefaultCategories();
                Save();
            }
        }

        private void EnsureDefaultCategories()
        {
            bool modified = false;
            var defaultCats = GetDefaultCategories();

            // 检查并重命名旧分类
            var oldGovCat = _categories.FirstOrDefault(c => c.Name == "政府官网");
            if (oldGovCat != null)
            {
                oldGovCat.Name = "政府信息官网";
                modified = true;
            }

            // 检查并重命名"办公网址"→"自定义网址"
            var oldOfficeCat = _categories.FirstOrDefault(c => c.Name == "办公网址");
            if (oldOfficeCat != null)
            {
                oldOfficeCat.Name = "自定义网址";
                modified = true;
            }

            // 检查并补充缺失的分类（如办公网址）
            foreach (var defaultCat in defaultCats)
            {
                // 跳过“政府信息官网”的添加，因为已重命名
                if (defaultCat.Name == "政府信息官网" && oldGovCat != null)
                    continue;
                if (!_categories.Any(c => c.Name == defaultCat.Name))
                {
                    _categories.Add(defaultCat);
                    modified = true;
                }
            }

            if (modified)
                Save();
        }

        private List<LinkCategory> GetDefaultCategories()
        {
            return new List<LinkCategory>
            {
                new LinkCategory
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "政府信息官网",
                    Links = new List<LinkItem>
                    {
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "住房和城乡建设部", Url = "https://www.mohurd.gov.cn/", Order = 1,
                            Description = "住建部官网，发布建筑业政策、资质标准、行业动态。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "住建部信息公开页", Url = "https://www.mohurd.gov.cn/gongkai/zc/index.html", Order = 2,
                            Description = "住建领域法规、标准、规范性文件集中公开。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "应急管理部", Url = "https://www.mem.gov.cn/", Order = 3,
                            Description = "应急管理、安全生产、防灾减灾政策与事故通报。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "交通运输部", Url = "https://www.mot.gov.cn/", Order = 4,
                            Description = "交通运输行业法规、标准、安全监管信息。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "生态环境部", Url = "https://www.mee.gov.cn", Order = 5,
                            Description = "环保法规、环评、排污许可、环境质量数据。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "国家法律法规数据库", Url = "https://flk.npc.gov.cn/index", Order = 6,
                            Description = "官方权威法律法规全文检索，覆盖面广。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "国家行政法规库", Url = "https://xzfg.moj.gov.cn/search2.html", Order = 7,
                            Description = "行政法规、规章文本查询，适用于执法依据。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "国家规章库", Url = "https://www.gov.cn/zhengce/xxgk/gjgzk/index.htm?searchWord=", Order = 8,
                            Description = "国务院部门规章、地方政府规章查询。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "全国标准信息公共服务平台（国家总库）", Url = "https://std.samr.gov.cn/", Order = 9,
                            Description = "国家标准、行业标准、地方标准、团体标准一站式查询。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "全国工程质量安全监管信息平台", Url = "https://zlaq.mohurd.gov.cn/fwmh/bjxcjgl/fwmh/pages/default/index.html", Order = 10,
                            Description = "工程质量安全监管、事故报告、信用评价。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "全国建筑市场监管公共服务平台", Url = "https://jzsc.mohurd.gov.cn/home", Order = 11,
                            Description = "建筑企业资质、注册人员、工程项目、诚信信息查询。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "中国人事考试网", Url = "http://www.cpta.com.cn/", Order = 12,
                            Description = "注册安全工程师、建造师等职业资格考试报名、成绩查询。" }
                    }
                },
                new LinkCategory
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "外部AI官网",
                    Links = new List<LinkItem>
                    {
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "豆包（字节跳动）", Url = "https://www.doubao.com/chat/", Order = 1,
                            Description = "情绪价值；中文强、支持语音/图片/视频总结、AI绘画；建筑/安全类文案、报告润色。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "文心（百度）", Url = "https://chat.baidu.com/", Order = 3,
                            Description = "国产AI绘图。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "通义千问（阿里）", Url = "https://tongyi.aliyun.com/qianwen/", Order = 4,
                            Description = "办公生态强、PPT/文档生成好；适合做安全方案、汇报材料。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "通义听悟（阿里）", Url = "https://tingwu.aliyun.com/home", Order = 5,
                            Description = "语音转写+总结；会议纪要、安全培训录音整理。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "讯飞星火（科大讯飞）", Url = "https://xinghuo.xfyun.cn/desk/", Order = 6,
                            Description = "语音交互强、多语种；适合语音转写、录音整理。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "智谱清言（智谱华章）", Url = "https://chatglm.cn/", Order = 7,
                            Description = "基于ChatGLM4，图表能力强；适合做安全数据统计、画图表。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "Kimi（月之暗面）", Url = "https://kimi.moonshot.cn/", Order = 8,
                            Description = "长文本之王（支持200万字）；可上传规范/标书/合同，适合研读JGJ、GB规范、做资料分析。" },
                        new LinkItem { Id = Guid.NewGuid().ToString(), Name = "DeepSeek（深度求索）", Url = "https://chat.deepseek.com/", Order = 9,
                            Description = "中文推理；适合写复杂安全计算书。" }
                    }
                },
                new LinkCategory
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "自定义网址",
                    Links = new List<LinkItem>()   // 空列表，用户自行添加
                }
            };
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_categories, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "保存 FavoriteLinks.json 失败");
            }
        }

        public List<LinkCategory> GetAllCategories() => _categories;

        public LinkCategory? GetCategoryById(string id) => _categories.FirstOrDefault(c => c.Id == id);

        public List<LinkItem> GetLinksByCategoryId(string categoryId)
        {
            var cat = GetCategoryById(categoryId);
            return cat?.Links.OrderBy(l => l.Order).ToList() ?? new List<LinkItem>();
        }

        public void AddCategory(string name)
        {
            var newId = Guid.NewGuid().ToString();
            _categories.Add(new LinkCategory { Id = newId, Name = name, Links = new List<LinkItem>() });
            Save();
        }

        public bool RenameCategory(string categoryId, string newName)
        {
            var cat = GetCategoryById(categoryId);
            if (cat == null) return false;
            cat.Name = newName;
            Save();
            return true;
        }

        public bool DeleteCategory(string categoryId)
        {
            var cat = GetCategoryById(categoryId);
            if (cat == null) return false;
            _categories.Remove(cat);
            Save();
            return true;
        }

        public void AddLink(string categoryId, string name, string url)
        {
            var cat = GetCategoryById(categoryId);
            if (cat == null) return;
            int maxOrder = cat.Links.Count > 0 ? cat.Links.Max(l => l.Order) : 0;
            var newLink = new LinkItem
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Url = url,
                Order = maxOrder + 1
            };
            cat.Links.Add(newLink);
            Save();
        }

        public bool UpdateLink(LinkItem link)
        {
            foreach (var cat in _categories)
            {
                var existing = cat.Links.FirstOrDefault(l => l.Id == link.Id);
                if (existing != null)
                {
                    existing.Name = link.Name;
                    existing.Url = link.Url;
                    Save();
                    return true;
                }
            }
            return false;
        }

        public bool DeleteLink(string linkId)
        {
            foreach (var cat in _categories)
            {
                var link = cat.Links.FirstOrDefault(l => l.Id == linkId);
                if (link != null)
                {
                    cat.Links.Remove(link);
                    // 重新排序
                    int order = 1;
                    foreach (var l in cat.Links.OrderBy(l => l.Order))
                    {
                        l.Order = order++;
                    }
                    Save();
                    return true;
                }
            }
            return false;
        }

        public bool MoveLinkUp(string categoryId, string linkId)
        {
            var cat = GetCategoryById(categoryId);
            if (cat == null) return false;
            var links = cat.Links.OrderBy(l => l.Order).ToList();
            var index = links.FindIndex(l => l.Id == linkId);
            if (index <= 0) return false;
            var prev = links[index - 1];
            var current = links[index];
            int temp = prev.Order;
            prev.Order = current.Order;
            current.Order = temp;
            Save();
            return true;
        }

        public bool MoveLinkDown(string categoryId, string linkId)
        {
            var cat = GetCategoryById(categoryId);
            if (cat == null) return false;
            var links = cat.Links.OrderBy(l => l.Order).ToList();
            var index = links.FindIndex(l => l.Id == linkId);
            if (index < 0 || index >= links.Count - 1) return false;
            var next = links[index + 1];
            var current = links[index];
            int temp = next.Order;
            next.Order = current.Order;
            current.Order = temp;
            Save();
            return true;
        }

        public bool MoveToTop(string categoryId, string linkId)
        {
            var cat = GetCategoryById(categoryId);
            if (cat == null) return false;
            var links = cat.Links.OrderBy(l => l.Order).ToList();
            var index = links.FindIndex(l => l.Id == linkId);
            if (index <= 0) return false;
            var target = links[index];
            foreach (var l in links.Take(index))
            {
                l.Order++;
            }
            target.Order = 1;
            Save();
            return true;
        }

        public bool MoveToBottom(string categoryId, string linkId)
        {
            var cat = GetCategoryById(categoryId);
            if (cat == null) return false;
            var links = cat.Links.OrderBy(l => l.Order).ToList();
            var index = links.FindIndex(l => l.Id == linkId);
            if (index < 0 || index == links.Count - 1) return false;
            var target = links[index];
            int maxOrder = links.Count;
            foreach (var l in links.Skip(index + 1))
            {
                l.Order--;
            }
            target.Order = maxOrder;
            Save();
            return true;
        }
    }
}