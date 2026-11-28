"use client";

import {
  AlertTriangle,
  ArchiveRestore,
  CheckCircle2,
  DatabaseBackup,
  Download,
  FileSpreadsheet,
  History,
  Image as ImageIcon,
  ShieldCheck,
  Trash2,
  Upload
} from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import { apiFetch, API_BASE_URL, compressImageFileToDataUrl, resolveMediaUrl } from "@/lib/api";

interface PreviewVariation {
  name: string;
  price: number;
  measureAmount?: number | null;
  measureUnit?: string | null;
}

interface PreviewIngredient {
  name: string;
  quantity: number;
  unitCode: string;
  unitCost: number;
}

interface PreviewModifierGroup {
  groupName: string;
  options: string[];
}

interface SpreadsheetPreviewRow {
  rowNumber: number;
  sku: string;
  categoryName: string;
  productName: string;
  description: string;
  price: number;
  promotionalPrice?: number | null;
  variations: PreviewVariation[];
  ingredients: PreviewIngredient[];
  modifierGroups: PreviewModifierGroup[];
  yieldText?: string | null;
  tagsCsv?: string | null;
  photoRef?: string | null;
  matchStatus: string;
  existingProductId?: string | null;
  existingPrice?: number | null;
  hasExistingSheet: boolean;
  errors: string[];
  warnings: string[];
}

interface UploadedImageItem {
  fileName: string;
  matchKey: string;
  mediaUrl: string;
}

interface BackupAdapterDto {
  adapterId: string;
  displayName: string;
  description: string;
  supportedExtensions: string[];
}

interface BackupAnalyzeDto {
  adapterId: string;
  adapterDisplayName: string;
  detectedFormat: string;
  totalRecordsFound: number;
  compatibleCount: number;
  duplicateCount: number;
  incompatibleCount: number;
  incompatibleNotes: string[];
  fieldMappings: Array<{ sourceField: string; pitakoTarget: string; populatedCount: number }>;
  previewRows: SpreadsheetPreviewRow[];
}

interface CleanupPreviewDto {
  protectedOrdersCount: number;
  categories: Array<{ id: string; name: string; productsCount: number }>;
  products: Array<{ id: string; name: string; categoryId: string; sku?: string | null; price: number }>;
  modifierGroups: Array<{ id: string; name: string; optionsCount: number }>;
  combos: Array<{ id: string; name: string; price: number }>;
  promotions: Array<{ id: string; name: string }>;
  supplies: Array<{ id: string; name: string; contentUnitCode: string }>;
  technicalSheets: Array<{ id: string; name: string; productId?: string | null }>;
  acaiBuilderCount: number;
}

interface ImportHistoryItem {
  id: string;
  sourceLabel: string;
  executedBy: string;
  executedAtUtc: string;
  importedOrAffectedCount: number;
  skippedOrPreservedCount: number;
  failedCount: number;
  summaryText: string;
}

interface ConfirmImportResultDto {
  sourceLabel: string;
  createdCount: number;
  updatedCount: number;
  skippedCount: number;
  failedCount: number;
  sheetsCreatedOrUpdated: number;
  imagesAttached: number;
  executedBy: string;
  executedAtUtc: string;
  errors: string[];
}

function readFileAsBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result || ""));
    reader.onerror = () => reject(new Error("Erro ao ler arquivo."));
    reader.readAsDataURL(file);
  });
}

export function AdminStoreDataImportCleanupPanel({
  token,
  storeTradeName,
  onNotify,
  onReload,
  onOpenCatalogTab
}: {
  token: string;
  storeTradeName: string;
  onNotify: (msg: string) => void;
  onReload: () => Promise<void>;
  onOpenCatalogTab: () => void;
}) {
  const [subTab, setSubTab] = useState<"spreadsheet" | "backup" | "cleanup">("spreadsheet");
  const [history, setHistory] = useState<ImportHistoryItem[]>([]);

  // Planilha
  const [uploadedImages, setUploadedImages] = useState<UploadedImageItem[]>([]);
  const [uploadingImages, setUploadingImages] = useState(false);
  const [previewInfo, setPreviewInfo] = useState<{
    fileName: string;
    detectedFormat: string;
    totalRows: number;
    newCount: number;
    existingCount: number;
    duplicateInFileCount: number;
    errorCount: number;
    existingSheetsConflictCount: number;
  } | null>(null);
  const [rows, setRows] = useState<SpreadsheetPreviewRow[]>([]);
  const [duplicateMode, setDuplicateMode] = useState<"skip" | "updateCatalog" | "updateAll">("skip");
  const [allowPriceAndSheetOverwrite, setAllowPriceAndSheetOverwrite] = useState(false);
  const [busyImport, setBusyImport] = useState(false);
  const [importResult, setImportResult] = useState<ConfirmImportResultDto | null>(null);

  // Backup de outro sistema
  const [adapters, setAdapters] = useState<BackupAdapterDto[]>([]);
  const [backupPreview, setBackupPreview] = useState<BackupAnalyzeDto | null>(null);
  const [categoryMap, setCategoryMap] = useState<Record<string, string>>({});
  const [backupDuplicateMode, setBackupDuplicateMode] = useState<"skip" | "updateCatalog" | "updateAll">("skip");
  const [backupAllowOverwrite, setBackupAllowOverwrite] = useState(false);
  const [backupResult, setBackupResult] = useState<ConfirmImportResultDto | null>(null);
  const [busyBackup, setBusyBackup] = useState(false);

  // Limpeza de dados iniciais
  const [cleanupPreview, setCleanupPreview] = useState<CleanupPreviewDto | null>(null);
  const [removeProducts, setRemoveProducts] = useState(true);
  const [removeCategories, setRemoveCategories] = useState(true);
  const [removeModifiers, setRemoveModifiers] = useState(true);
  const [removeTechnicalSheets, setRemoveTechnicalSheets] = useState(true);
  const [removeSupplies, setRemoveSupplies] = useState(true);
  const [removeCombos, setRemoveCombos] = useState(true);
  const [removePromotions, setRemovePromotions] = useState(true);
  const [removeAcaiBuilder, setRemoveAcaiBuilder] = useState(false);
  const [createSafetyBackup, setCreateSafetyBackup] = useState(true);
  const [confirmPhrase, setConfirmPhrase] = useState("");
  const [busyCleanup, setBusyCleanup] = useState(false);
  const [cleanupSummary, setCleanupSummary] = useState<{
    safetyBackupCode?: string | null;
    productsRemoved: number;
    productsArchivedForOrders: number;
    categoriesRemoved: number;
    sheetsRemoved: number;
    suppliesRemoved: number;
    modifiersRemoved: number;
    combosRemoved: number;
  } | null>(null);

  const loadMetadata = useCallback(async () => {
    if (!token) return;
    const [histRes, adpRes, clnRes] = await Promise.all([
      apiFetch<{ history: ImportHistoryItem[] }>("/api/v1/store-data/import/history", {}, token),
      apiFetch<{ adapters: BackupAdapterDto[] }>("/api/v1/store-data/backup-import/adapters", {}, token),
      apiFetch<CleanupPreviewDto>("/api/v1/store-data/starter-cleanup/preview", {}, token)
    ]);
    if (histRes.ok && histRes.data?.history) setHistory(histRes.data.history);
    if (adpRes.ok && adpRes.data?.adapters) setAdapters(adpRes.data.adapters);
    if (clnRes.ok && clnRes.data) setCleanupPreview(clnRes.data);
  }, [token]);

  useEffect(() => {
    void loadMetadata();
  }, [loadMetadata]);

  async function downloadTemplate(format: "csv" | "xls") {
    const resp = await fetch(`${API_BASE_URL}/api/v1/store-data/import/template?format=${format}`, {
      headers: { Authorization: `Bearer ${token}` }
    });
    if (!resp.ok) {
      onNotify("Não foi possível baixar o modelo de planilha.");
      return;
    }
    const blob = await resp.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = format === "xls" ? "modelo-importacao-pitako.xls" : "modelo-importacao-pitako.csv";
    a.click();
    URL.revokeObjectURL(url);
    onNotify(`Modelo ${format.toUpperCase()} baixado com exemplos de preenchimento!`);
  }

  async function handleUploadImageFilesOrZip(e: React.ChangeEvent<HTMLInputElement>) {
    const fileList = e.target.files;
    if (!fileList || fileList.length === 0) return;
    setUploadingImages(true);
    try {
      const filesArr = Array.from(fileList);
      const zipFile = filesArr.find((f) => f.name.toLowerCase().endsWith(".zip"));
      const imageFiles = filesArr.filter((f) => /\.(png|jpe?g|webp)$/i.test(f.name)).slice(0, 80);

      let zipBase64: string | null = null;
      if (zipFile) {
        zipBase64 = await readFileAsBase64(zipFile);
      }

      const encodedFiles: Array<{ fileName: string; base64DataUrl: string }> = [];
      for (const f of imageFiles) {
        const compressed = await compressImageFileToDataUrl(f, 720, 0.82);
        encodedFiles.push({ fileName: f.name, base64DataUrl: compressed });
      }

      const res = await apiFetch<{ uploadedCount: number; images: UploadedImageItem[] }>(
        "/api/v1/store-data/import/images-batch",
        { method: "POST", body: JSON.stringify({ zipBase64, files: encodedFiles }) },
        token
      );
      if (res.ok && res.data?.images) {
        setUploadedImages((prev) => [...prev, ...res.data!.images]);
        onNotify(`${res.data.uploadedCount} foto(s) validada(s) e pronta(s) para associação aos produtos!`);
      } else {
        onNotify(res.errorMessage || "Erro ao enviar fotos em lote.");
      }
    } catch {
      onNotify("Erro ao processar pacote de imagens.");
    }
    setUploadingImages(false);
    e.target.value = "";
  }

  async function handleSpreadsheetUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    setImportResult(null);
    setBusyImport(true);
    try {
      const base64 = await readFileAsBase64(file);
      const res = await apiFetch<{
        fileName: string;
        detectedFormat: string;
        totalRows: number;
        newCount: number;
        existingCount: number;
        duplicateInFileCount: number;
        errorCount: number;
        existingSheetsConflictCount: number;
        rows: SpreadsheetPreviewRow[];
      }>(
        "/api/v1/store-data/import/spreadsheet/preview",
        { method: "POST", body: JSON.stringify({ fileName: file.name, fileBase64: base64 }) },
        token
      );
      if (res.ok && res.data) {
        setPreviewInfo(res.data);
        setRows(res.data.rows);
        onNotify(`Planilha lida (${res.data.detectedFormat}): ${res.data.totalRows} linha(s) na prévia!`);
      } else {
        onNotify(res.errorMessage || "Erro ao validar planilha.");
      }
    } catch {
      onNotify("Falha ao ler o arquivo selecionado.");
    }
    setBusyImport(false);
    e.target.value = "";
  }

  function updateRowField(index: number, patch: Partial<SpreadsheetPreviewRow>) {
    setRows((prev) =>
      prev.map((row, idx) => {
        if (idx !== index) return row;
        const next = { ...row, ...patch };
        const errs: string[] = [];
        if (!next.productName.trim()) errs.push("Nome do produto é obrigatório.");
        if (!next.price || next.price <= 0) errs.push("Preço deve ser maior que zero.");
        return {
          ...next,
          errors: errs,
          matchStatus: errs.length > 0 ? "Error" : row.existingProductId ? "ExistingMatch" : "New"
        };
      })
    );
  }

  async function confirmSpreadsheetImport() {
    const validRows = rows.filter((r) => r.errors.length === 0);
    if (validRows.length === 0) {
      onNotify("Corrija os erros das linhas antes de confirmar a importação.");
      return;
    }
    if (duplicateMode === "updateAll" && !allowPriceAndSheetOverwrite) {
      onNotify("Marque a caixa de autorização para substituir preços e fichas técnicas existentes.");
      return;
    }

    setBusyImport(true);
    const res = await apiFetch<ConfirmImportResultDto>(
      "/api/v1/store-data/import/spreadsheet/confirm",
      {
        method: "POST",
        body: JSON.stringify({
          sourceLabel: `Planilha (${previewInfo?.fileName || "manual"})`,
          duplicateMode,
          allowPriceAndSheetOverwrite: duplicateMode === "updateAll" && allowPriceAndSheetOverwrite,
          rows: validRows,
          imageMappings: uploadedImages
        })
      },
      token
    );
    setBusyImport(false);
    if (res.ok && res.data) {
      setImportResult(res.data);
      onNotify(`Importação concluída: ${res.data.createdCount} novos e ${res.data.updatedCount} atualizados!`);
      await Promise.all([onReload(), loadMetadata()]);
    } else {
      onNotify(res.errorMessage || "Erro ao confirmar importação.");
    }
  }

  async function handleBackupUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    setBackupResult(null);
    setBusyBackup(true);
    try {
      const base64 = await readFileAsBase64(file);
      const res = await apiFetch<BackupAnalyzeDto>(
        "/api/v1/store-data/backup-import/analyze",
        { method: "POST", body: JSON.stringify({ fileName: file.name, fileBase64: base64 }) },
        token
      );
      if (res.ok && res.data) {
        setBackupPreview(res.data);
        const initialCats: Record<string, string> = {};
        res.data.previewRows.forEach((r) => {
          if (r.categoryName && !initialCats[r.categoryName]) initialCats[r.categoryName] = r.categoryName;
        });
        setCategoryMap(initialCats);
        onNotify(`Backup analisado via ${res.data.adapterDisplayName}: ${res.data.compatibleCount} item(ns) compatíveis.`);
      } else {
        onNotify(res.errorMessage || "Formato de backup não suportado.");
      }
    } catch {
      onNotify("Erro ao ler arquivo de backup.");
    }
    setBusyBackup(false);
    e.target.value = "";
  }

  async function confirmBackupMigration() {
    if (!backupPreview) return;
    if (backupDuplicateMode === "updateAll" && !backupAllowOverwrite) {
      onNotify("Marque a autorização para sobrescrever preços e fichas existentes.");
      return;
    }

    const validRows = backupPreview.previewRows.filter((r) => r.errors.length === 0);
    setBusyBackup(true);
    const res = await apiFetch<ConfirmImportResultDto>(
      "/api/v1/store-data/backup-import/confirm",
      {
        method: "POST",
        body: JSON.stringify({
          adapterId: backupPreview.adapterId,
          duplicateMode: backupDuplicateMode,
          allowPriceAndSheetOverwrite: backupDuplicateMode === "updateAll" && backupAllowOverwrite,
          categoryMap,
          rows: validRows
        })
      },
      token
    );
    setBusyBackup(false);
    if (res.ok && res.data) {
      setBackupResult(res.data);
      onNotify(`Migração de backup concluída: ${res.data.createdCount} criados, ${res.data.updatedCount} atualizados!`);
      await Promise.all([onReload(), loadMetadata()]);
    } else {
      onNotify(res.errorMessage || "Erro ao executar migração.");
    }
  }

  async function executeStarterCleanup() {
    if (confirmPhrase.trim() !== "LIMPAR") {
      onNotify("Digite exatamente LIMPAR no campo de confirmação.");
      return;
    }
    setBusyCleanup(true);
    const res = await apiFetch<{
      safetyBackupCode?: string | null;
      productsRemoved: number;
      productsArchivedForOrders: number;
      categoriesRemoved: number;
      sheetsRemoved: number;
      suppliesRemoved: number;
      modifiersRemoved: number;
      combosRemoved: number;
    }>(
      "/api/v1/store-data/starter-cleanup/execute",
      {
        method: "POST",
        body: JSON.stringify({
          confirmPhrase: confirmPhrase.trim(),
          createSafetyBackup,
          removeCombos,
          removePromotions,
          removeTechnicalSheets,
          removeProducts,
          removeModifiers,
          removeCategories,
          removeSupplies,
          removeAcaiBuilder
        })
      },
      token
    );
    setBusyCleanup(false);
    if (res.ok && res.data) {
      setCleanupSummary(res.data);
      setConfirmPhrase("");
      onNotify("Limpeza inicial executada com segurança! Pedidos e históricos preservados.");
      await Promise.all([onReload(), loadMetadata()]);
    } else {
      onNotify(res.errorMessage || "Erro ao limpar dados iniciais.");
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
        <div>
          <h2 className="text-base font-extrabold text-slate-900">
            Central de Dados da Loja ({storeTradeName})
          </h2>
          <p className="text-xs text-slate-500">
            Importe catálogo e fichas técnicas por planilha com fotos, migre backups de sistemas compatíveis ou limpe dados iniciais com segurança.
          </p>
        </div>

        <div className="flex flex-wrap gap-2">
          {[
            { key: "spreadsheet", label: "Importar por Planilha", icon: FileSpreadsheet },
            { key: "backup", label: "Importar backup de outro sistema", icon: ArchiveRestore },
            { key: "cleanup", label: "Limpar dados iniciais da loja", icon: Trash2 }
          ].map((t) => {
            const Icon = t.icon;
            const active = subTab === t.key;
            return (
              <button
                key={t.key}
                type="button"
                onClick={() => setSubTab(t.key as "spreadsheet" | "backup" | "cleanup")}
                style={active ? { backgroundColor: "var(--store-primary)", color: "#fff" } : undefined}
                className={`inline-flex items-center gap-1.5 rounded-xl px-3.5 py-2 text-xs font-extrabold transition ${
                  active ? "shadow-sm" : "border border-slate-200 bg-white text-slate-700 hover:bg-slate-50"
                }`}
              >
                <Icon className="h-3.5 w-3.5" />
                {t.label}
              </button>
            );
          })}
        </div>
      </div>

      {/* 1. IMPORTAÇÃO POR PLANILHA */}
      {subTab === "spreadsheet" && (
        <div className="space-y-5">
          <div className="grid gap-4 lg:grid-cols-3">
            <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3">
              <p className="text-xs font-extrabold uppercase tracking-wide text-slate-500">1. Modelo de Planilha</p>
              <p className="text-xs text-slate-600">
                Baixe a planilha oficial com colunas para produtos, preços, categorias, variações, ingredientes, custos, adicionais e referência de foto.
              </p>
              <div className="flex flex-wrap gap-2">
                <button
                  type="button"
                  onClick={() => void downloadTemplate("csv")}
                  className="inline-flex items-center gap-1.5 rounded-xl bg-slate-900 px-3 py-2 text-xs font-bold text-white hover:bg-slate-800"
                >
                  <Download className="h-3.5 w-3.5" /> Baixar Modelo CSV
                </button>
                <button
                  type="button"
                  onClick={() => void downloadTemplate("xls")}
                  className="inline-flex items-center gap-1.5 rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs font-bold text-slate-800 hover:bg-slate-50"
                >
                  <Download className="h-3.5 w-3.5" /> Baixar Modelo Excel (.xls)
                </button>
              </div>
            </div>

            <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3">
              <p className="text-xs font-extrabold uppercase tracking-wide text-slate-500">2. Fotos dos Produtos (Opcional)</p>
              <p className="text-xs text-slate-600">
                Envie múltiplas fotos PNG/JPG/WebP ou um pacote <strong>.zip</strong>. O nome do arquivo (ou coluna <code className="rounded bg-slate-100 px-1">CodigoFoto</code>) vincula cada foto ao produto.
              </p>
              <label className="inline-flex cursor-pointer items-center gap-1.5 rounded-xl border border-indigo-300 bg-indigo-50 px-3.5 py-2 text-xs font-extrabold text-indigo-800 hover:bg-indigo-100">
                <ImageIcon className="h-3.5 w-3.5" />
                {uploadingImages ? "Processando imagens..." : "Enviar Fotos ou Pacote (.zip)"}
                <input
                  type="file"
                  multiple
                  accept=".png,.jpg,.jpeg,.webp,.zip"
                  onChange={handleUploadImageFilesOrZip}
                  className="hidden"
                />
              </label>
              {uploadedImages.length > 0 && (
                <div className="flex flex-wrap gap-1.5 pt-1">
                  {uploadedImages.slice(0, 8).map((img, i) => (
                    <div key={`${img.mediaUrl}-${i}`} title={img.fileName} className="flex items-center gap-1 rounded-lg border border-slate-200 bg-slate-50 px-1.5 py-1 text-[10px]">
                      <img src={resolveMediaUrl(img.mediaUrl) || ""} alt={img.fileName} className="h-6 w-6 rounded object-contain bg-white" />
                      <span className="max-w-[80px] truncate">{img.fileName}</span>
                    </div>
                  ))}
                  {uploadedImages.length > 8 && (
                    <span className="self-center text-[10px] font-bold text-slate-500">+{uploadedImages.length - 8} foto(s)</span>
                  )}
                </div>
              )}
            </div>

            <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-3">
              <p className="text-xs font-extrabold uppercase tracking-wide text-slate-500">3. Enviar Planilha para Prévia</p>
              <p className="text-xs text-slate-600">
                Aceita <strong>.csv</strong>, <strong>.xls</strong> e <strong>.xlsx</strong> (máx. 10 MB). Os dados serão validados antes de gravar.
              </p>
              <label className="inline-flex cursor-pointer items-center gap-1.5 rounded-xl bg-emerald-600 px-4 py-2 text-xs font-extrabold text-white hover:bg-emerald-700">
                <Upload className="h-3.5 w-3.5" />
                {busyImport ? "Analisando planilha..." : "Selecionar Planilha (.csv / .xls / .xlsx)"}
                <input
                  type="file"
                  accept=".csv,.xls,.xlsx,.txt"
                  onChange={handleSpreadsheetUpload}
                  className="hidden"
                />
              </label>
            </div>
          </div>

          {previewInfo && rows.length > 0 && (
            <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div>
                  <h3 className="text-sm font-extrabold text-slate-900">
                    Prévia e Revisão antes de Confirmar ({previewInfo.fileName} · {previewInfo.detectedFormat})
                  </h3>
                  <p className="text-xs text-slate-500">
                    Corrija eventuais erros diretamente na tabela abaixo antes de importar.
                  </p>
                </div>
                <div className="flex flex-wrap gap-2 text-xs font-bold">
                  <span className="rounded-full bg-emerald-100 px-2.5 py-1 text-emerald-800">{previewInfo.newCount} novos</span>
                  <span className="rounded-full bg-amber-100 px-2.5 py-1 text-amber-800">{previewInfo.existingCount} já existentes</span>
                  <span className="rounded-full bg-rose-100 px-2.5 py-1 text-rose-800">
                    {rows.filter((r) => r.errors.length > 0).length} erro(s)
                  </span>
                </div>
              </div>

              {/* Opções de conflito e segurança */}
              <div className="rounded-xl border border-amber-200 bg-amber-50/70 p-3.5 text-xs space-y-2">
                <p className="font-extrabold text-amber-950">Tratamento de Produtos Duplicados / Existentes:</p>
                <div className="flex flex-wrap gap-3">
                  <label className="inline-flex items-center gap-1.5 font-semibold text-slate-800">
                    <input type="radio" checked={duplicateMode === "skip"} onChange={() => setDuplicateMode("skip")} />
                    Ignorar produtos já existentes (não duplicar)
                  </label>
                  <label className="inline-flex items-center gap-1.5 font-semibold text-slate-800">
                    <input type="radio" checked={duplicateMode === "updateCatalog"} onChange={() => setDuplicateMode("updateCatalog")} />
                    Atualizar descrição/categoria/fotos mantendo preços e fichas atuais
                  </label>
                  <label className="inline-flex items-center gap-1.5 font-semibold text-rose-800">
                    <input type="radio" checked={duplicateMode === "updateAll"} onChange={() => setDuplicateMode("updateAll")} />
                    Atualizar tudo (incluindo preços e fichas técnicas existentes)
                  </label>
                </div>
                {duplicateMode === "updateAll" && (
                  <label className="inline-flex items-center gap-2 font-bold text-rose-900">
                    <input
                      type="checkbox"
                      checked={allowPriceAndSheetOverwrite}
                      onChange={(e) => setAllowPriceAndSheetOverwrite(e.target.checked)}
                    />
                    Autorizo expressamente substituir preços e fichas técnicas já cadastradas ({previewInfo.existingSheetsConflictCount} ficha(s) envolvida(s)).
                  </label>
                )}
              </div>

              <div className="overflow-x-auto rounded-xl border border-slate-200">
                <table className="w-full text-left text-xs">
                  <thead className="bg-slate-50 text-slate-600">
                    <tr>
                      <th className="px-2.5 py-2">#</th>
                      <th className="px-2.5 py-2">Status</th>
                      <th className="px-2.5 py-2">SKU</th>
                      <th className="px-2.5 py-2">Categoria</th>
                      <th className="px-2.5 py-2">Produto</th>
                      <th className="px-2.5 py-2">Preço (R$)</th>
                      <th className="px-2.5 py-2">Ficha / Opções</th>
                      <th className="px-2.5 py-2">Foto Associada</th>
                      <th className="px-2.5 py-2">Validação</th>
                      <th className="px-2.5 py-2" />
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {rows.map((r, idx) => (
                      <tr key={`${r.rowNumber}-${idx}`} className={r.errors.length > 0 ? "bg-rose-50/70" : ""}>
                        <td className="px-2.5 py-2 font-mono">{r.rowNumber}</td>
                        <td className="px-2.5 py-2">
                          <span
                            className={`rounded-full px-2 py-0.5 text-[10px] font-extrabold ${
                              r.errors.length > 0
                                ? "bg-rose-600 text-white"
                                : r.existingProductId
                                  ? "bg-amber-100 text-amber-800"
                                  : "bg-emerald-100 text-emerald-800"
                            }`}
                          >
                            {r.errors.length > 0 ? "Erro" : r.existingProductId ? "Existente" : "Novo"}
                          </span>
                        </td>
                        <td className="px-2.5 py-2">
                          <input
                            value={r.sku}
                            onChange={(e) => updateRowField(idx, { sku: e.target.value })}
                            className="w-20 rounded border border-slate-200 px-1.5 py-1 font-mono text-[11px]"
                          />
                        </td>
                        <td className="px-2.5 py-2">
                          <input
                            value={r.categoryName}
                            onChange={(e) => updateRowField(idx, { categoryName: e.target.value })}
                            className="w-28 rounded border border-slate-200 px-1.5 py-1 text-[11px]"
                          />
                        </td>
                        <td className="px-2.5 py-2">
                          <input
                            value={r.productName}
                            onChange={(e) => updateRowField(idx, { productName: e.target.value })}
                            className="w-40 rounded border border-slate-200 px-1.5 py-1 font-bold text-[11px]"
                          />
                        </td>
                        <td className="px-2.5 py-2">
                          <input
                            type="number"
                            step="0.01"
                            value={r.price}
                            onChange={(e) => updateRowField(idx, { price: Number(e.target.value) })}
                            className="w-20 rounded border border-slate-200 px-1.5 py-1 font-mono text-[11px]"
                          />
                        </td>
                        <td className="px-2.5 py-2 text-[10px] text-slate-600">
                          {r.variations.length > 0 && <div>{r.variations.length} variação(ões)</div>}
                          {r.ingredients.length > 0 && <div>{r.ingredients.length} ingrediente(s)</div>}
                          {r.modifierGroups.length > 0 && <div>{r.modifierGroups.length} grupo(s) opc.</div>}
                        </td>
                        <td className="px-2.5 py-2">
                          {uploadedImages.length > 0 ? (
                            <select
                              value={r.photoRef || ""}
                              onChange={(e) => updateRowField(idx, { photoRef: e.target.value })}
                              className="w-32 rounded border border-slate-200 px-1.5 py-1 text-[10px]"
                            >
                              <option value="">Sem vínculo ({r.photoRef || "vazio"})</option>
                              {uploadedImages.map((im) => (
                                <option key={im.mediaUrl} value={im.fileName}>
                                  {im.fileName}
                                </option>
                              ))}
                            </select>
                          ) : (
                            <span className="text-[10px] text-slate-500">{r.photoRef || "Sem ref."}</span>
                          )}
                        </td>
                        <td className="px-2.5 py-2 text-[10px]">
                          {r.errors.map((er, i) => (
                            <p key={i} className="font-bold text-rose-700">{er}</p>
                          ))}
                          {r.warnings.map((w, i) => (
                            <p key={i} className="text-amber-700">{w}</p>
                          ))}
                        </td>
                        <td className="px-2.5 py-2">
                          <button
                            type="button"
                            onClick={() => setRows((prev) => prev.filter((_, i) => i !== idx))}
                            className="rounded p-1 text-rose-600 hover:bg-rose-100"
                            title="Remover linha da prévia"
                          >
                            <Trash2 className="h-3.5 w-3.5" />
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="flex flex-wrap items-center justify-between gap-3">
                <button
                  type="button"
                  onClick={() => {
                    setPreviewInfo(null);
                    setRows([]);
                  }}
                  className="rounded-xl border border-slate-300 px-4 py-2 text-xs font-bold text-slate-700 hover:bg-slate-50"
                >
                  Cancelar Prévia
                </button>
                <button
                  type="button"
                  disabled={busyImport}
                  onClick={() => void confirmSpreadsheetImport()}
                  className="inline-flex items-center gap-1.5 rounded-xl bg-emerald-600 px-5 py-2.5 text-xs font-extrabold text-white hover:bg-emerald-700 disabled:opacity-50"
                >
                  <CheckCircle2 className="h-4 w-4" /> {busyImport ? "Importando..." : "Confirmar e Importar Produtos"}
                </button>
              </div>
            </div>
          )}

          {importResult && (
            <div className="rounded-2xl border border-emerald-200 bg-emerald-50 p-5 text-xs text-emerald-950 space-y-2">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <p className="text-sm font-extrabold">Resultado da Importação</p>
                <button
                  type="button"
                  onClick={onOpenCatalogTab}
                  className="rounded-xl bg-emerald-700 px-3.5 py-1.5 font-extrabold text-white hover:bg-emerald-800"
                >
                  Editar Produtos Importados no Cardápio
                </button>
              </div>
              <p>
                Responsável: <strong>{importResult.executedBy}</strong> em{" "}
                {new Date(importResult.executedAtUtc).toLocaleString("pt-BR")}
              </p>
              <div className="flex flex-wrap gap-4 font-bold">
                <span>Novos criados: {importResult.createdCount}</span>
                <span>Atualizados: {importResult.updatedCount}</span>
                <span>Duplicados ignorados: {importResult.skippedCount}</span>
                <span>Fichas técnicas: {importResult.sheetsCreatedOrUpdated}</span>
                <span>Fotos vinculadas: {importResult.imagesAttached}</span>
              </div>
            </div>
          )}
        </div>
      )}

      {/* 2. IMPORTAÇÃO DE BACKUP DE OUTRO SISTEMA */}
      {subTab === "backup" && (
        <div className="space-y-5">
          <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-4">
            <div>
              <h3 className="text-sm font-extrabold text-slate-900">Importadores e Adaptadores Implementados</h3>
              <p className="text-xs text-slate-500">
                Arquitetura extensível por adaptadores: listamos somente formatos testados e compatíveis no servidor.
              </p>
            </div>
            <div className="grid gap-3 md:grid-cols-3">
              {adapters.map((adp) => (
                <div key={adp.adapterId} className="rounded-xl border border-slate-200 bg-slate-50 p-3.5 text-xs">
                  <p className="font-extrabold text-slate-900">{adp.displayName}</p>
                  <p className="mt-1 text-slate-600">{adp.description}</p>
                  <p className="mt-2 font-mono text-[10px] font-bold text-indigo-700">
                    Extensões: {adp.supportedExtensions.join(", ")}
                  </p>
                </div>
              ))}
            </div>

            <div className="border-t border-slate-100 pt-4">
              <label className="inline-flex cursor-pointer items-center gap-2 rounded-xl bg-slate-900 px-4 py-2.5 text-xs font-extrabold text-white hover:bg-slate-800">
                <Upload className="h-4 w-4" />
                {busyBackup ? "Analisando estrutura do backup..." : "Selecionar e Analisar Arquivo de Backup"}
                <input
                  type="file"
                  accept=".json,.csv,.xls,.xlsx,.txt"
                  onChange={handleBackupUpload}
                  className="hidden"
                />
              </label>
            </div>
          </div>

          {backupPreview && (
            <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm space-y-4 text-xs">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div>
                  <h4 className="text-sm font-extrabold text-slate-900">
                    Prévia da Migração ({backupPreview.detectedFormat})
                  </h4>
                  <p className="text-slate-500">
                    Compatíveis: <strong>{backupPreview.compatibleCount}</strong> · Duplicados:{" "}
                    <strong>{backupPreview.duplicateCount}</strong> · Incompatíveis/Erros:{" "}
                    <strong>{backupPreview.incompatibleCount}</strong>
                  </p>
                </div>
              </div>

              {backupPreview.incompatibleNotes.length > 0 && (
                <div className="rounded-xl border border-amber-200 bg-amber-50 p-3 text-amber-900">
                  {backupPreview.incompatibleNotes.map((n, i) => (
                    <p key={i}>• {n}</p>
                  ))}
                </div>
              )}

              <div className="grid gap-4 md:grid-cols-2">
                <div className="rounded-xl border border-slate-200 p-3">
                  <p className="mb-2 font-extrabold text-slate-800">Mapeamento de Campos Detectado</p>
                  <ul className="space-y-1 text-slate-600">
                    {backupPreview.fieldMappings.map((fm, i) => (
                      <li key={i} className="flex justify-between">
                        <span>{fm.sourceField} → <strong>{fm.pitakoTarget}</strong></span>
                        <span className="font-mono">{fm.populatedCount} reg.</span>
                      </li>
                    ))}
                  </ul>
                </div>

                <div className="rounded-xl border border-slate-200 p-3 space-y-2">
                  <p className="font-extrabold text-slate-800">Relacionar Categorias para o PITAKO</p>
                  <div className="max-h-36 space-y-1.5 overflow-y-auto">
                    {Object.keys(categoryMap).map((origCat) => (
                      <div key={origCat} className="flex items-center gap-2">
                        <span className="w-1/2 truncate text-slate-600">{origCat}</span>
                        <span>→</span>
                        <input
                          value={categoryMap[origCat]}
                          onChange={(e) => setCategoryMap((prev) => ({ ...prev, [origCat]: e.target.value }))}
                          className="w-1/2 rounded border border-slate-200 px-2 py-1 text-[11px] font-bold"
                        />
                      </div>
                    ))}
                  </div>
                </div>
              </div>

              <div className="flex flex-wrap gap-3 rounded-xl border border-slate-200 bg-slate-50 p-3">
                <label className="inline-flex items-center gap-1.5 font-semibold">
                  <input type="radio" checked={backupDuplicateMode === "skip"} onChange={() => setBackupDuplicateMode("skip")} />
                  Ignorar duplicados
                </label>
                <label className="inline-flex items-center gap-1.5 font-semibold">
                  <input type="radio" checked={backupDuplicateMode === "updateCatalog"} onChange={() => setBackupDuplicateMode("updateCatalog")} />
                  Atualizar catálogo mantendo preços atuais
                </label>
                <label className="inline-flex items-center gap-1.5 font-semibold text-rose-800">
                  <input type="radio" checked={backupDuplicateMode === "updateAll"} onChange={() => setBackupDuplicateMode("updateAll")} />
                  Substituir preços/fichas existentes
                </label>
                {backupDuplicateMode === "updateAll" && (
                  <label className="inline-flex items-center gap-1.5 font-bold text-rose-900">
                    <input type="checkbox" checked={backupAllowOverwrite} onChange={(e) => setBackupAllowOverwrite(e.target.checked)} />
                    Autorizo atualizar preços/fichas existentes
                  </label>
                )}
              </div>

              <div className="flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setBackupPreview(null)}
                  className="rounded-xl border border-slate-300 px-4 py-2 font-bold text-slate-700"
                >
                  Cancelar Operação
                </button>
                <button
                  type="button"
                  disabled={busyBackup}
                  onClick={() => void confirmBackupMigration()}
                  className="rounded-xl bg-emerald-600 px-5 py-2 font-extrabold text-white hover:bg-emerald-700 disabled:opacity-50"
                >
                  {busyBackup ? "Migrando..." : `Confirmar Migração (${backupPreview.compatibleCount} itens)`}
                </button>
              </div>
            </div>
          )}

          {backupResult && (
            <div className="rounded-2xl border border-emerald-200 bg-emerald-50 p-4 text-xs text-emerald-950">
              <p className="font-extrabold">Relatório da Migração Concluída</p>
              <p className="mt-1">
                {backupResult.createdCount} novos criados · {backupResult.updatedCount} atualizados ·{" "}
                {backupResult.skippedCount} duplicados ignorados · {backupResult.failedCount} erro(s).
              </p>
            </div>
          )}
        </div>
      )}

      {/* 3. LIMPAR DADOS INICIAIS DA LOJA */}
      {subTab === "cleanup" && (
        <div className="space-y-5">
          <div className="rounded-2xl border border-rose-200 bg-white p-5 shadow-sm space-y-4 text-xs">
            <div className="flex items-start gap-3 rounded-xl border border-emerald-200 bg-emerald-50 p-3.5 text-emerald-950">
              <ShieldCheck className="mt-0.5 h-5 w-5 shrink-0 text-emerald-700" />
              <div>
                <p className="font-extrabold">Proteção Absoluta do Histórico Real</p>
                <p className="mt-0.5">
                  Atualmente existem <strong>{cleanupPreview?.protectedOrdersCount ?? 0} pedido(s)</strong> nesta loja. Pedidos, vendas de PDV, pagamentos, usuários, logs e dados de outras lojas <strong>nunca serão apagados</strong>. Produtos vinculados a pedidos reais são apenas arquivados.
                </p>
              </div>
            </div>

            <div>
              <h3 className="text-sm font-extrabold text-slate-900">Selecione exatamente o que deseja limpar desta loja:</h3>
              <div className="mt-3 grid gap-2.5 sm:grid-cols-2 lg:grid-cols-4">
                {[
                  { label: `Produtos (${cleanupPreview?.products.length ?? 0})`, checked: removeProducts, set: setRemoveProducts },
                  { label: `Categorias (${cleanupPreview?.categories.length ?? 0})`, checked: removeCategories, set: setRemoveCategories },
                  { label: `Insumos / Ingredientes (${cleanupPreview?.supplies.length ?? 0})`, checked: removeSupplies, set: setRemoveSupplies },
                  { label: `Fichas Técnicas (${cleanupPreview?.technicalSheets.length ?? 0})`, checked: removeTechnicalSheets, set: setRemoveTechnicalSheets },
                  { label: `Adicionais e Complementos (${cleanupPreview?.modifierGroups.length ?? 0})`, checked: removeModifiers, set: setRemoveModifiers },
                  { label: `Combos (${cleanupPreview?.combos.length ?? 0})`, checked: removeCombos, set: setRemoveCombos },
                  { label: `Promoções (${cleanupPreview?.promotions.length ?? 0})`, checked: removePromotions, set: setRemovePromotions },
                  { label: `Montador Açaí (${cleanupPreview?.acaiBuilderCount ?? 0})`, checked: removeAcaiBuilder, set: setRemoveAcaiBuilder }
                ].map((opt) => (
                  <label key={opt.label} className="flex items-center gap-2 rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5 font-bold text-slate-800">
                    <input type="checkbox" checked={opt.checked} onChange={(e) => opt.set(e.target.checked)} />
                    <span>{opt.label}</span>
                  </label>
                ))}
              </div>
            </div>

            <label className="flex items-center gap-2 rounded-xl border border-indigo-200 bg-indigo-50/70 px-3.5 py-2.5 font-bold text-indigo-950">
              <DatabaseBackup className="h-4 w-4 text-indigo-700" />
              <input type="checkbox" checked={createSafetyBackup} onChange={(e) => setCreateSafetyBackup(e.target.checked)} />
              Gerar backup criptografado de segurança antes de executar a limpeza (recomendado)
            </label>

            <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-rose-200 bg-rose-50/60 p-4">
              <div className="flex items-center gap-2">
                <AlertTriangle className="h-4 w-4 shrink-0 text-rose-700" />
                <span className="font-bold text-rose-950">Para confirmar, digite LIMPAR:</span>
                <input
                  value={confirmPhrase}
                  onChange={(e) => setConfirmPhrase(e.target.value)}
                  placeholder="LIMPAR"
                  className="w-28 rounded-lg border border-rose-300 bg-white px-2.5 py-1.5 font-mono font-extrabold uppercase text-rose-900"
                />
              </div>
              <button
                type="button"
                disabled={busyCleanup || confirmPhrase.trim() !== "LIMPAR"}
                onClick={() => void executeStarterCleanup()}
                className="rounded-xl bg-rose-600 px-4 py-2 font-extrabold text-white hover:bg-rose-700 disabled:opacity-40"
              >
                {busyCleanup ? "Limpando com segurança..." : "Executar Limpeza Segura"}
              </button>
            </div>
          </div>

          {cleanupSummary && (
            <div className="rounded-2xl border border-emerald-200 bg-emerald-50 p-4 text-xs text-emerald-950 space-y-1">
              <p className="font-extrabold">Limpeza Concluída e Registrada em Auditoria</p>
              {cleanupSummary.safetyBackupCode && (
                <p>Backup pré-limpeza gerado: <strong className="font-mono">{cleanupSummary.safetyBackupCode}</strong></p>
              )}
              <p>
                Removidos: {cleanupSummary.productsRemoved} produto(s) ({cleanupSummary.productsArchivedForOrders} arquivados por possuírem pedidos), {cleanupSummary.categoriesRemoved} categoria(s), {cleanupSummary.sheetsRemoved} ficha(s), {cleanupSummary.suppliesRemoved} insumo(s), {cleanupSummary.modifiersRemoved} grupo(s), {cleanupSummary.combosRemoved} combo(s).
              </p>
            </div>
          )}
        </div>
      )}

      {/* HISTÓRICO AUDITÁVEL DA LOJA */}
      {history.length > 0 && (
        <div className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
          <h3 className="mb-3 flex items-center gap-2 text-sm font-extrabold text-slate-900">
            <History className="h-4 w-4 text-slate-600" /> Histórico Recente de Importações & Limpezas ({storeTradeName})
          </h3>
          <div className="space-y-2 text-xs">
            {history.slice(0, 8).map((h) => (
              <div key={h.id} className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-slate-100 bg-slate-50 px-3.5 py-2">
                <div>
                  <span className="font-extrabold text-slate-900">{h.sourceLabel}</span>{" "}
                  <span className="text-slate-500">por {h.executedBy}</span>
                  <p className="text-[11px] text-slate-600">{h.summaryText}</p>
                </div>
                <span className="font-mono text-[11px] text-slate-500">
                  {new Date(h.executedAtUtc).toLocaleString("pt-BR")}
                </span>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
