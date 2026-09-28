(function () {
    if (!window.Chart) return;

    Chart.defaults.color = '#52657d';
    Chart.defaults.font.family = 'Inter, Segoe UI, Arial, sans-serif';
    Chart.defaults.font.size = 11;
    Chart.defaults.plugins.legend.labels.usePointStyle = true;
    Chart.defaults.plugins.legend.labels.padding = 14;
    Chart.defaults.scale.grid.color = 'rgba(148, 163, 184, 0.18)';
})();
