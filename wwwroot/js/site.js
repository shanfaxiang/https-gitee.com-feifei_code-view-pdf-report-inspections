// 顶栏实时日期时间
(function () {
    function pad(n) { return n < 10 ? '0' + n : n; }
    function tick() {
        var d = new Date();
        var dateEl = document.getElementById('topDate');
        var timeEl = document.getElementById('topTime');
        if (dateEl) dateEl.textContent = d.getFullYear() + '年' + (d.getMonth() + 1) + '月' + d.getDate() + '日';
        if (timeEl) timeEl.textContent = pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
    }
    tick();
    setInterval(tick, 1000);
})();
