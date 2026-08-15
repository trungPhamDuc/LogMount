// Global helper to export any Chart.js canvas to PNG image with white background
window.downloadChartAsImage = function (canvasId, defaultFileName) {
    var canvas = document.getElementById(canvasId);
    if (!canvas) {
        alert('Không tìm thấy biểu đồ để tải xuống!');
        return;
    }
    var tempCanvas = document.createElement('canvas');
    tempCanvas.width = canvas.width;
    tempCanvas.height = canvas.height;
    var ctx = tempCanvas.getContext('2d');
    
    // Fill solid white background
    ctx.fillStyle = '#ffffff';
    ctx.fillRect(0, 0, tempCanvas.width, tempCanvas.height);
    
    // Draw canvas content over white background
    ctx.drawImage(canvas, 0, 0);

    var link = document.createElement('a');
    link.download = defaultFileName || 'bieu-do.png';
    link.href = tempCanvas.toDataURL('image/png', 1.0);
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

// Load RetryImprove History
window.loadRetryImproveHistory = function() {
    var tbody = document.getElementById('retryImproveHistoryTable');
    if (!tbody) return;
    
    tbody.innerHTML = '<tr><td colspan="10" class="text-center py-4"><div class="spinner-border spinner-border-sm text-primary" role="status"></div> Đang tải dữ liệu...</td></tr>';
    
    fetch('/api/improve/retry/list')
        .then(function(r) { return r.json(); })
        .then(function(list) {
            if (!list || list.length === 0) {
                tbody.innerHTML = '<tr><td colspan="10" class="text-center py-4 text-muted">Chưa có nhật ký hành động cải thiện nào được lưu.</td></tr>';
                return;
            }
            var html = '';
            list.forEach(function(item) {
                var dateStr = item.executionDate ? new Date(item.executionDate).toLocaleDateString('vi-VN') : '';
                var isoDate = item.executionDate ? item.executionDate.substring(0, 10) : '';
                html += '<tr>' +
                    '<td class="fw-bold text-success">' + dateStr + '</td>' +
                    '<td class="fw-bold text-primary">' + (item.partsName || '') + '</td>' +
                    '<td>' + (item.line || '') + '</td>' +
                    '<td>' + (item.lane || '') + '</td>' +
                    '<td>' + (item.side || '') + '</td>' +
                    '<td>' + (item.machine || '') + '</td>' +
                    '<td>' + (item.feeder || '') + '</td>' +
                    '<td class="fw-bold">' + (item.engineerName || '') + '</td>' +
                    '<td class="text-wrap" style="max-width: 300px;">' + (item.actionTaken || '') + '</td>' +
                    '<td class="text-center text-nowrap">' +
                        '<button type="button" class="btn btn-sm btn-outline-warning me-1 py-0 px-2 fs-7" onclick="openRetryImproveModal(\'' + (item.partsName || '') + '\', \'' + (item.line || '') + '\', \'' + (item.lane || '') + '\', \'' + (item.side || '') + '\', \'' + (item.machine || '') + '\', \'' + (item.feeder || '') + '\', ' + item.id + ', \'' + (item.engineerName || '') + '\', \'' + (item.actionTaken || '').replace(/'/g, "\\'") + '\', \'' + isoDate + '\')"><i class="bi bi-pencil"></i> Sửa</button>' +
                        '<button type="button" class="btn btn-sm btn-outline-danger py-0 px-2 fs-7" onclick="deleteRetryImprove(' + item.id + ')"><i class="bi bi-trash"></i> Xóa</button>' +
                    '</td>' +
                    '</tr>';
            });
            tbody.innerHTML = html;
        })
        .catch(function(err) {
            console.error(err);
            tbody.innerHTML = '<tr><td colspan="10" class="text-center py-4 text-danger">Lỗi khi tải nhật ký cải thiện.</td></tr>';
        });
};

// Load ErrorImprove History
window.loadErrorImproveHistory = function() {
    var tbody = document.getElementById('errorImproveHistoryTable');
    if (!tbody) return;
    
    tbody.innerHTML = '<tr><td colspan="9" class="text-center py-4"><div class="spinner-border spinner-border-sm text-primary" role="status"></div> Đang tải dữ liệu...</td></tr>';
    
    fetch('/api/improve/error/list')
        .then(function(r) { return r.json(); })
        .then(function(list) {
            if (!list || list.length === 0) {
                tbody.innerHTML = '<tr><td colspan="9" class="text-center py-4 text-muted">Chưa có nhật ký hành động cải thiện nào được lưu.</td></tr>';
                return;
            }
            var html = '';
            list.forEach(function(item) {
                var dateStr = item.executionDate ? new Date(item.executionDate).toLocaleDateString('vi-VN') : '';
                var isoDate = item.executionDate ? item.executionDate.substring(0, 10) : '';
                html += '<tr>' +
                    '<td class="fw-bold text-success">' + dateStr + '</td>' +
                    '<td class="fw-bold text-danger">' + (item.error || '') + '</td>' +
                    '<td>' + (item.line || '') + '</td>' +
                    '<td>' + (item.lane || '') + '</td>' +
                    '<td>' + (item.side || '') + '</td>' +
                    '<td>' + (item.machine || '') + '</td>' +
                    '<td class="fw-bold">' + (item.engineerName || '') + '</td>' +
                    '<td class="text-wrap" style="max-width: 300px;">' + (item.actionTaken || '') + '</td>' +
                    '<td class="text-center text-nowrap">' +
                        '<button type="button" class="btn btn-sm btn-outline-warning me-1 py-0 px-2 fs-7" onclick="openErrorImproveModal(\'' + (item.error || '') + '\', \'' + (item.line || '') + '\', \'' + (item.lane || '') + '\', \'' + (item.side || '') + '\', \'' + (item.machine || '') + '\', ' + item.id + ', \'' + (item.engineerName || '') + '\', \'' + (item.actionTaken || '').replace(/'/g, "\\'") + '\', \'' + isoDate + '\')"><i class="bi bi-pencil"></i> Sửa</button>' +
                        '<button type="button" class="btn btn-sm btn-outline-danger py-0 px-2 fs-7" onclick="deleteErrorImprove(' + item.id + ')"><i class="bi bi-trash"></i> Xóa</button>' +
                    '</td>' +
                    '</tr>';
            });
            tbody.innerHTML = html;
        })
        .catch(function(err) {
            console.error(err);
            tbody.innerHTML = '<tr><td colspan="9" class="text-center py-4 text-danger">Lỗi khi tải nhật ký cải thiện.</td></tr>';
        });
};

window.refreshImprovementLists = function() {
    window.loadRetryImproveHistory();
    window.loadErrorImproveHistory();
};

// RetryLog Improvement Modal Open & Save Logic
window.openRetryImproveModal = function(partsName, line, lane, side, machine, feeder, id, engineerName, actionTaken, executionDate, isReadOnly) {
    var readOnly = (typeof isReadOnly === 'boolean') 
        ? isReadOnly 
        : (Boolean(partsName) && (!id || id === 0));

    var idEl = document.getElementById('retryImp_Id');
    var partsNameEl = document.getElementById('retryImp_PartsName');
    var lineEl = document.getElementById('retryImp_Line');
    var laneEl = document.getElementById('retryImp_Lane');
    var sideEl = document.getElementById('retryImp_Side');
    var machineEl = document.getElementById('retryImp_Machine');
    var feederEl = document.getElementById('retryImp_Feeder');
    var engineerNameEl = document.getElementById('retryImp_EngineerName');
    var actionTakenEl = document.getElementById('retryImp_ActionTaken');
    var executionDateEl = document.getElementById('retryImp_ExecutionDate');
    var titleEl = document.getElementById('retryImproveModalLabel');

    if (titleEl) {
        titleEl.innerHTML = (id && id > 0)
            ? '<i class="bi bi-pencil-square"></i> Cập Nhật Hành Động Cải Thiện RetryLog'
            : (readOnly ? '<i class="bi bi-tools"></i> Lưu Hành Động Cải Thiện RetryLog' : '<i class="bi bi-plus-circle"></i> Thêm Hành Động Cải Thiện RetryLog');
    }

    var fields = [partsNameEl, lineEl, laneEl, sideEl, machineEl, feederEl];
    fields.forEach(function(el) {
        if (el) {
            if (readOnly) {
                el.setAttribute('readonly', 'readonly');
                el.classList.add('bg-light');
            } else {
                el.removeAttribute('readonly');
                el.classList.remove('bg-light');
            }
        }
    });

    if (idEl) idEl.value = id || '0';
    if (partsNameEl) partsNameEl.value = partsName || '';
    if (lineEl) lineEl.value = line || '';
    if (laneEl) laneEl.value = lane || '';
    if (sideEl) sideEl.value = side || '';
    if (machineEl) machineEl.value = machine || '';
    if (feederEl) feederEl.value = feeder || '';
    if (engineerNameEl) engineerNameEl.value = engineerName || '';
    if (actionTakenEl) actionTakenEl.value = actionTaken || '';
    if (executionDateEl) {
        if (executionDate) {
            executionDateEl.value = executionDate.substring(0, 10);
        } else {
            executionDateEl.value = new Date().toISOString().substring(0, 10);
        }
    }
    
    var modalEl = document.getElementById('retryImproveModal');
    if (modalEl) {
        var modal = new bootstrap.Modal(modalEl);
        modal.show();
    }
};

window.submitRetryImproveForm = function() {
    var id = parseInt(document.getElementById('retryImp_Id')?.value || '0', 10);
    var partsName = document.getElementById('retryImp_PartsName')?.value || '';
    var line = document.getElementById('retryImp_Line')?.value || '';
    var lane = document.getElementById('retryImp_Lane')?.value || '';
    var side = document.getElementById('retryImp_Side')?.value || '';
    var machine = document.getElementById('retryImp_Machine')?.value || '';
    var feeder = document.getElementById('retryImp_Feeder')?.value || '';
    var engineerName = document.getElementById('retryImp_EngineerName')?.value || '';
    var actionTaken = document.getElementById('retryImp_ActionTaken')?.value || '';
    var executionDate = document.getElementById('retryImp_ExecutionDate')?.value || '';

    if (!engineerName.trim() || !actionTaken.trim() || !executionDate) {
        alert('Vui lòng điền đầy đủ các thông tin bắt buộc (*)');
        return;
    }

    var data = {
        partsName: partsName,
        line: line,
        lane: lane,
        side: side,
        machine: machine,
        feeder: feeder,
        engineerName: engineerName,
        actionTaken: actionTaken,
        executionDate: executionDate
    };

    var url = id > 0 ? ('/api/improve/retry/' + id) : '/api/improve/retry';
    var method = id > 0 ? 'PUT' : 'POST';

    fetch(url, {
        method: method,
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(data)
    })
    .then(function (r) { return r.json(); })
    .then(function (res) {
        if (res.success) {
            alert(res.message);
            var modalEl = document.getElementById('retryImproveModal');
            var modal = bootstrap.Modal.getInstance(modalEl);
            if (modal) modal.hide();
            window.location.reload();
        } else {
            alert('Lỗi: ' + (res.message || 'Không thể lưu.'));
        }
    })
    .catch(function (err) {
        console.error(err);
        alert('Lỗi kết nối máy chủ!');
    });
};

window.deleteRetryImprove = function(id) {
    if (!confirm('Bạn có chắc chắn muốn xóa nhật ký hành động cải thiện này không?')) {
        return;
    }
    fetch('/api/improve/retry/' + id, {
        method: 'DELETE'
    })
    .then(function(r) { return r.json(); })
    .then(function(res) {
        if (res.success) {
            alert(res.message);
            window.location.reload();
        } else {
            alert('Lỗi: ' + (res.message || 'Không thể xóa.'));
        }
    })
    .catch(function(err) {
        console.error(err);
        alert('Lỗi kết nối máy chủ!');
    });
};

// ErrorLog Improvement Modal Open & Save Logic
window.openErrorImproveModal = function(error, line, lane, side, machine, id, engineerName, actionTaken, executionDate, isReadOnly) {
    var readOnly = (typeof isReadOnly === 'boolean') 
        ? isReadOnly 
        : (Boolean(error) && (!id || id === 0));

    var idEl = document.getElementById('errorImp_Id');
    var errorEl = document.getElementById('errorImp_Error');
    var lineEl = document.getElementById('errorImp_Line');
    var laneEl = document.getElementById('errorImp_Lane');
    var sideEl = document.getElementById('errorImp_Side');
    var machineEl = document.getElementById('errorImp_Machine');
    var engineerNameEl = document.getElementById('errorImp_EngineerName');
    var actionTakenEl = document.getElementById('errorImp_ActionTaken');
    var executionDateEl = document.getElementById('errorImp_ExecutionDate');
    var titleEl = document.getElementById('errorImproveModalLabel');

    if (titleEl) {
        titleEl.innerHTML = (id && id > 0)
            ? '<i class="bi bi-pencil-square"></i> Cập Nhật Hành Động Cải Thiện ErrorLog'
            : (readOnly ? '<i class="bi bi-tools"></i> Lưu Hành Động Cải Thiện ErrorLog' : '<i class="bi bi-plus-circle"></i> Thêm Hành Động Cải Thiện ErrorLog');
    }

    var fields = [errorEl, lineEl, laneEl, sideEl, machineEl];
    fields.forEach(function(el) {
        if (el) {
            if (readOnly) {
                el.setAttribute('readonly', 'readonly');
                el.classList.add('bg-light');
            } else {
                el.removeAttribute('readonly');
                el.classList.remove('bg-light');
            }
        }
    });

    if (idEl) idEl.value = id || '0';
    if (errorEl) errorEl.value = error || '';
    if (lineEl) lineEl.value = line || '';
    if (laneEl) laneEl.value = lane || '';
    if (sideEl) sideEl.value = side || '';
    if (machineEl) machineEl.value = machine || '';
    if (engineerNameEl) engineerNameEl.value = engineerName || '';
    if (actionTakenEl) actionTakenEl.value = actionTaken || '';
    if (executionDateEl) {
        if (executionDate) {
            executionDateEl.value = executionDate.substring(0, 10);
        } else {
            executionDateEl.value = new Date().toISOString().substring(0, 10);
        }
    }
    
    var modalEl = document.getElementById('errorImproveModal');
    if (modalEl) {
        var modal = new bootstrap.Modal(modalEl);
        modal.show();
    }
};

window.submitErrorImproveForm = function() {
    var id = parseInt(document.getElementById('errorImp_Id')?.value || '0', 10);
    var error = document.getElementById('errorImp_Error')?.value || '';
    var line = document.getElementById('errorImp_Line')?.value || '';
    var lane = document.getElementById('errorImp_Lane')?.value || '';
    var side = document.getElementById('errorImp_Side')?.value || '';
    var machine = document.getElementById('errorImp_Machine')?.value || '';
    var engineerName = document.getElementById('errorImp_EngineerName')?.value || '';
    var actionTaken = document.getElementById('errorImp_ActionTaken')?.value || '';
    var executionDate = document.getElementById('errorImp_ExecutionDate')?.value || '';

    if (!engineerName.trim() || !actionTaken.trim() || !executionDate) {
        alert('Vui lòng điền đầy đủ các thông tin bắt buộc (*)');
        return;
    }

    var data = {
        error: error,
        line: line,
        lane: lane,
        side: side,
        machine: machine,
        engineerName: engineerName,
        actionTaken: actionTaken,
        executionDate: executionDate
    };

    var url = id > 0 ? ('/api/improve/error/' + id) : '/api/improve/error';
    var method = id > 0 ? 'PUT' : 'POST';

    fetch(url, {
        method: method,
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(data)
    })
    .then(function (r) { return r.json(); })
    .then(function (res) {
        if (res.success) {
            alert(res.message);
            var modalEl = document.getElementById('errorImproveModal');
            var modal = bootstrap.Modal.getInstance(modalEl);
            if (modal) modal.hide();
            window.location.reload();
        } else {
            alert('Lỗi: ' + (res.message || 'Không thể lưu.'));
        }
    })
    .catch(function (err) {
        console.error(err);
        alert('Lỗi kết nối máy chủ!');
    });
};

window.deleteErrorImprove = function(id) {
    if (!confirm('Bạn có chắc chắn muốn xóa nhật ký hành động cải thiện này không?')) {
        return;
    }
    fetch('/api/improve/error/' + id, {
        method: 'DELETE'
    })
    .then(function(r) { return r.json(); })
    .then(function(res) {
        if (res.success) {
            alert(res.message);
            window.location.reload();
        } else {
            alert('Lỗi: ' + (res.message || 'Không thể xóa.'));
        }
    })
    .catch(function(err) {
        console.error(err);
        alert('Lỗi kết nối máy chủ!');
    });
};

// Cumulative multi-file upload
(function () {
    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    function formatFileSize(bytes) {
        if (bytes < 1024) {
            return bytes + ' B';
        }

        if (bytes < 1024 * 1024) {
            return (bytes / 1024).toFixed(1) + ' KB';
        }

        return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
    }

    function initCumulativeFileUpload(inputId, listId, formId) {
        const input = document.getElementById(inputId);
        const list = document.getElementById(listId);
        const form = document.getElementById(formId);

        if (!input || !list || !form) {
            return;
        }

        let accumulatedFiles = [];

        function fileKey(file) {
            return file.name + '|' + file.size + '|' + file.lastModified;
        }

        function syncInput() {
            const dataTransfer = new DataTransfer();
            accumulatedFiles.forEach(function (file) {
                dataTransfer.items.add(file);
            });
            input.files = dataTransfer.files;
        }

        function renderList() {
            list.innerHTML = '';

            if (accumulatedFiles.length === 0) {
                list.classList.add('d-none');
                return;
            }

            list.classList.remove('d-none');

            accumulatedFiles.forEach(function (file, index) {
                const item = document.createElement('li');
                item.className = 'list-group-item d-flex justify-content-between align-items-center py-2 px-0 border-0';
                item.innerHTML =
                    '<span class="text-truncate me-2">' +
                    escapeHtml(file.name) +
                    ' <span class="text-muted">(' + formatFileSize(file.size) + ')</span></span>' +
                    '<button type="button" class="btn btn-sm btn-outline-danger flex-shrink-0" data-index="' + index + '">Xóa</button>';
                list.appendChild(item);
            });

            list.querySelectorAll('button[data-index]').forEach(function (button) {
                button.addEventListener('click', function () {
                    const index = parseInt(button.getAttribute('data-index'), 10);
                    accumulatedFiles.splice(index, 1);
                    syncInput();
                    renderList();
                });
            });
        }

        input.addEventListener('change', function () {
            const existingKeys = new Set(accumulatedFiles.map(fileKey));

            Array.from(input.files).forEach(function (file) {
                const key = fileKey(file);
                if (!existingKeys.has(key)) {
                    accumulatedFiles.push(file);
                    existingKeys.add(key);
                }
            });

            syncInput();
            renderList();
            input.value = '';
        });

        form.addEventListener('submit', function (event) {
            const submitter = event.submitter;
            const formAction = submitter && submitter.getAttribute('formaction');

            if (formAction && formAction.toLowerCase().includes('clear')) {
                return;
            }

            syncInput();

            if (accumulatedFiles.length === 0) {
                event.preventDefault();
                window.alert('Vui lòng chọn ít nhất một file.');
            }
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        initCumulativeFileUpload('log-upload-input', 'log-upload-file-list', 'log-upload-form');
        
        // Auto-load history list if table placeholder is present
        if (document.getElementById('retryImproveHistoryTable')) {
            window.loadRetryImproveHistory();
        }
        if (document.getElementById('errorImproveHistoryTable')) {
            window.loadErrorImproveHistory();
        }
    });
})();
