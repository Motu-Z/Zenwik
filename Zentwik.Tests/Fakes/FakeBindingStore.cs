using System.Collections.Generic;
using System.Linq;
using Zentwik.Core.Helpers;

namespace Zentwik.Tests.Fakes
{
    /// <summary>内存版 IBindingStore：Save 记下最后一次规则集，Profile 由它派生。</summary>
    public sealed class FakeBindingStore : IBindingStore
    {
        private List<BindingRule> _rules = new();
        public BindingProfile Profile => new() { Rules = _rules };
        public bool HasEnabledRules => _rules.Any(r => r.Enabled);

        /// <summary>最后一次 Save 的规则集；null 表示还没保存过。</summary>
        public IReadOnlyList<BindingRule>? LastSaved { get; private set; }

        public void Load() { }
        public void ResetToFactory() { }
        public void Reload() { }

        public void Save(IEnumerable<BindingRule> rules)
        {
            _rules = rules.ToList();
            LastSaved = _rules;
        }

        public BindingRule? LookupByName(string name) => _rules.FirstOrDefault(r => r.Name == name);
    }
}
